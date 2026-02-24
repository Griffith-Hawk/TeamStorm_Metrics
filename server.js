require('dotenv').config({ path: require('path').join(__dirname, '.env') });
const express = require('express');
const cors = require('cors');
const fetch = require('node-fetch');
const path = require('path');
// === Расчёт Факта по STORM_HISTORY_API.md и example.py ===
// 1. История: GET history/api/v1/workspaces/{id}/workItems/{id}/history (Cookie session)
// 2. Фильтр: type === "StatusUpdated"
// 3. Сумма: время в статусе "in progress" (Пн–Пт 08:00–17:00 МСК)
// 4. Только History API, без атрибутов Storm — результат в колонку Факт
const MOSCOW_OFFSET_MS = 3 * 60 * 60 * 1000; // UTC+3
const WORK_START = 8;
const WORK_END = 17;
const IN_PROGRESS_STATUS = 'in progress';

/** Все варианты буквы O, похожие на латинскую (кириллица о/О, греческая ο, fullwidth и др.) */
const O_LIKE_CHARS = /[\u043E\u041E\u03BF\u04E9\u04EB\uFF4F\u1D0F\u0275]/g;

/** Нормализует статус: заменяет все варианты "о" на латинскую (in prоgress → in progress) */
function normalizeStatusForInProgress(s) {
  if (!s || typeof s !== 'string') return '';
  return String(s)
    .replace(O_LIKE_CHARS, 'o')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

function isInProgressStatus(status) {
  const norm = normalizeStatusForInProgress(status);
  return norm === IN_PROGRESS_STATUS || norm === 'in  progress'; // на случай двойного пробела
}

// Факт QA: только статус "testing". Явно исключаем "ready to test"
function isTestingStatus(status) {
  const norm = normalizeStatusForInProgress(status);
  if (norm === 'ready to test') return false;
  return norm === 'testing';
}

function isInAssessmentTestingStatus(status) {
  const norm = normalizeStatusForInProgress(status);
  return norm === 'in assessment testing';
}

/** ready to test или dev (включая dev waiting) — попала в тесты */
function isReadyToTestOrDev(status) {
  const norm = normalizeStatusForInProgress(status);
  return norm === 'ready to test' || norm === 'dev' || norm === 'dev waiting';
}

/** Из события истории извлекаем displayName того, кто сделал смену статуса. */
function getEventUserDisplayName(e) {
  const u = e?.user ?? e?.author ?? e?.createdBy ?? e?.updatedBy ?? e?.data?.user ?? e?.data?.author;
  if (u && typeof u === 'object' && u.displayName) return u.displayName;
  if (u && typeof u === 'object' && u.name) return u.name;
  if (typeof u === 'string') return u;
  const id = e?.userId ?? e?.authorId ?? e?.data?.userId;
  if (id) return id;
  return null;
}

/** Пары (попала в тесты, взяли в тест, лаг). История: дата и статус при смене. */
function computeReadyToTestPairs(history) {
  const events = [];
  for (const e of history) {
    const t = (e.type || '').toLowerCase();
    if (t !== 'statusupdated' && t !== 'workitemstatusupdated') continue;
    const nv = e?.data?.newValue;
    const raw = (nv?.statusName ?? nv?.name ?? (typeof nv === 'string' ? nv : '')) || '';
    const newStatus = raw ? normalizeStatusForInProgress(raw) || raw : '';
    if (!e.date || !newStatus) continue;
    events.push([new Date(e.date), newStatus, e]);
  }
  if (!events.length) return [];
  events.sort((a, b) => a[0].getTime() - b[0].getTime());

  const pairs = [];
  let lastReadyAt = null;
  let lastReadyEvent = null;
  const now = Date.now();
  for (let i = 0; i < events.length; i++) {
    const [dt, status, ev] = events[i];
    if (isReadyToTestOrDev(status)) {
      lastReadyAt = dt;
      lastReadyEvent = ev;
    } else if (isTestingStatus(status)) {
      const readyAt = lastReadyAt || (i > 0 ? events[i - 1][0] : dt);
      const readyEvent = lastReadyAt ? lastReadyEvent : (i > 0 ? events[i - 1][2] : null);
      const lagMs = Math.max(0, dt.getTime() - readyAt.getTime());
      const nextDt = i + 1 < events.length ? events[i + 1][0] : new Date(now);
      const testingMs = Math.max(0, nextDt.getTime() - dt.getTime());
      pairs.push({
        readyAt: readyAt.toISOString(),
        takenAt: dt.toISOString(),
        lagMs,
        lagHours: Math.round(lagMs / 36e5 * 10) / 10,
        testingMs,
        testingHours: Math.round(testingMs / 36e5 * 10) / 10,
        readyBy: getEventUserDisplayName(readyEvent),
        takenBy: getEventUserDisplayName(ev)
      });
      lastReadyAt = null;
      lastReadyEvent = null;
    }
  }
  return pairs;
}

function moscowWeekday(d) {
  return new Date(d.getTime() + MOSCOW_OFFSET_MS).getUTCDay();
}

function isWorkingDay(d) {
  const dow = moscowWeekday(d);
  return dow >= 1 && dow <= 5;
}

function moscowDayStart(d) {
  const m = new Date(d.getTime() + MOSCOW_OFFSET_MS);
  const y = m.getUTCFullYear(), mon = m.getUTCMonth(), day = m.getUTCDate();
  return new Date(Date.UTC(y, mon, day, WORK_START - 3, 0, 0, 0));
}

function moscowDayEnd(d) {
  const m = new Date(d.getTime() + MOSCOW_OFFSET_MS);
  const y = m.getUTCFullYear(), mon = m.getUTCMonth(), day = m.getUTCDate();
  return new Date(Date.UTC(y, mon, day, WORK_END - 3, 0, 0, 0));
}

function nextMoscowDayStart(d) {
  const m = new Date(d.getTime() + MOSCOW_OFFSET_MS);
  const y = m.getUTCFullYear(), mon = m.getUTCMonth(), day = m.getUTCDate();
  return new Date(Date.UTC(y, mon, day + 1, WORK_START - 3, 0, 0, 0));
}

function addWorkingTimeSegment(startDt, endDt) {
  if (endDt <= startDt) return 0;
  let totalMs = 0;
  let cur = new Date(startDt);
  const end = new Date(endDt);

  while (cur.getTime() <= end.getTime()) {
    if (!isWorkingDay(cur)) {
      cur = nextMoscowDayStart(cur);
      continue;
    }
    const dayStart = moscowDayStart(cur);
    const dayEnd = moscowDayEnd(cur);
    const segStart = Math.max(cur.getTime(), dayStart.getTime());
    const segEnd = Math.min(end.getTime(), dayEnd.getTime());
    if (segStart < segEnd) totalMs += segEnd - segStart;
    cur = nextMoscowDayStart(cur);
  }
  return totalMs / 1000;
}

/** QA-активности (testing, in assessment testing): считаем всё время 24/7 без окна 8–17 */
function addFullDaySegment(startDt, endDt) {
  if (endDt <= startDt) return 0;
  return (endDt.getTime() - startDt.getTime()) / 1000;
}

function calculateTimeInStatusMinutes(history, isTargetStatusFn, segmentFn) {
  const addSegment = segmentFn || addWorkingTimeSegment;
  const events = [];
  for (const e of history) {
    const nv = e?.data?.newValue;
    const raw = (nv?.statusName ?? nv?.name ?? (typeof nv === 'string' ? nv : '')) || '';
    const newStatus = normalizeStatusForInProgress(raw) || raw;
    if (!e.date || !newStatus) continue;
    events.push([new Date(e.date), newStatus]);
  }
  if (!events.length) return 0;
  events.sort((a, b) => a[0].getTime() - b[0].getTime());

  const periodStart = events[0][0];
  const periodEnd = new Date();

  let inTarget = false;
  for (const [dt, status] of events) {
    if (dt <= periodStart) inTarget = isTargetStatusFn(status);
    else break;
  }

  let lastTs = periodStart;
  let totalSec = 0;

  for (const [dt, status] of events) {
    if (dt <= periodStart) continue;
    if (dt > periodEnd) {
      if (inTarget) totalSec += addSegment(lastTs, periodEnd);
      break;
    }
    if (inTarget) totalSec += addSegment(lastTs, dt);
    inTarget = isTargetStatusFn(status);
    lastTs = dt;
  }
  if (lastTs < periodEnd && inTarget) {
    totalSec += addSegment(lastTs, periodEnd);
  }
  return totalSec / 60;
}

function calculateInProgressMinutes(history) {
  return calculateTimeInStatusMinutes(history, isInProgressStatus);
}

function calculateTestingMinutes(history) {
  return calculateTimeInStatusMinutes(history, isTestingStatus, addFullDaySegment);
}

function calculateInAssessmentTestingMinutes(history) {
  return calculateTimeInStatusMinutes(history, isInAssessmentTestingStatus, addFullDaySegment);
}

async function fetchHistoryForWorkitem(workspaceId, workitemId, auth) {
  const baseUrls = [
    `https://storm.alabuga.space/history/api/v1/workspaces/${workspaceId}/workItems/${workitemId}/history`,
    `https://storm.alabuga.space/history/api/v1/workspaces/${workspaceId}/workitems/${workitemId}/history`
  ];
  const opts = { headers: { 'Content-Type': 'application/json' }, ...auth };

  const fetchPage = async (url) => {
    const res = await fetch(url, opts);
    if (!res.ok) return null;
    return res.json().catch(() => null);
  };

  const toEvents = (data) => {
    if (!data) return [];
    if (Array.isArray(data)) return data;
    return data.items || data.data || data.entries || [];
  };

  for (const baseUrl of baseUrls) {
    let all = [];
    let url = baseUrl;
    let page = 0;
    const maxPages = 50;
    while (url && page < maxPages) {
      const data = await fetchPage(url);
      if (!data) break;
      const events = toEvents(data);
      all = all.concat(events);
      const nextToken = data.nextToken || data.nextPageToken || data.continuationToken;
      url = (nextToken && typeof nextToken === 'string') ? `${baseUrl}${baseUrl.includes('?') ? '&' : '?'}fromToken=${encodeURIComponent(nextToken)}` : null;
      if (!nextToken || events.length === 0) break;
      page++;
    }
    if (all.length > 0 || page > 0) return all;
  }
  return null;
}

async function runWithConcurrency(tasks, concurrency = 8) {
  const results = [];
  let i = 0;
  const worker = async () => {
    while (i < tasks.length) {
      const idx = i++;
      const t = tasks[idx];
      if (!t) break;
      try {
        await t();
      } catch (e) {
        console.error('Spent-time task error:', e.message);
      }
    }
  };
  await Promise.all(Array(Math.min(concurrency, tasks.length)).fill(null).map(worker));
}

const app = express();
const PORT = process.env.PORT || 3000;
const STORM_API = 'https://storm.alabuga.space/admin/api/v1/workspaces/get';

app.use(cors());
app.use(express.json());

// API routes — до static, чтобы /api/* не отдавал index.html
// Важно: workitems (самый длинный путь) — перед folders
app.get('/api/health', (req, res) => {
  res.json({ ok: true });
});

// Спринты папки (релизы в папке) — явный маршрут
app.get('/api/workspaces/:workspaceId/folders/:folderId/sprints', async (req, res) => {
  const apiToken = (process.env.STORM_API_TOKEN || '').trim();
  const { workspaceId, folderId } = req.params;
  const { maxItemsCount } = req.query;

  if (!apiToken) {
    return res.status(500).json({ error: 'STORM_API_TOKEN not configured' });
  }

  const params = new URLSearchParams();
  params.set('folderId', folderId);
  if (maxItemsCount) params.set('maxItemsCount', Math.min(100, maxItemsCount));
  const url = `https://storm.alabuga.space/cwm/public/api/v1/workspaces/${encodeURIComponent(workspaceId)}/sprints?${params}`;

  try {
    const response = await fetch(url, {
      headers: { 'Content-Type': 'application/json', 'Authorization': `PrivateToken ${apiToken}` }
    });
    const text = await response.text();
    if (!response.ok) {
      let details;
      try {
        const errJson = JSON.parse(text);
        details = errJson.messages?.join('; ') || errJson.key || text.slice(0, 300);
      } catch { details = text.slice(0, 300); }
      return res.status(response.status).json({ error: 'Storm sprints API error', status: response.status, details });
    }
    let data;
    try { data = JSON.parse(text); } catch { return res.status(502).json({ error: 'Некорректный ответ Storm' }); }
    res.json(data);
  } catch (err) {
    console.error('Sprints API failed:', err.message);
    res.status(502).json({ error: 'Failed to fetch sprints', details: err.message });
  }
});

// POST workitems/fact — ДО workitems/:id, иначе "fact" матчится как workitemId
app.post('/api/workspaces/:workspaceId/workitems/fact', async (req, res) => {
  const ids = req.body?.workitemIds;
  const sessionToken = process.env.STORM_SESSION_TOKEN;
  const apiToken = (process.env.STORM_API_TOKEN || '').trim();
  const { workspaceId } = req.params;

  if (!Array.isArray(ids) || ids.length === 0) {
    return res.status(400).json({ error: 'workitemIds must be a non-empty array' });
  }
  const auth = sessionToken
    ? { headers: { 'Cookie': `session=${sessionToken}` } }
    : apiToken
      ? { headers: { 'Authorization': `PrivateToken ${apiToken}` } }
      : null;
  if (!auth) {
    return res.status(500).json({
      error: 'STORM_SESSION_TOKEN or STORM_API_TOKEN required',
      hint: 'History API требует сессию. Добавьте STORM_SESSION_TOKEN в .env (значение cookie session из браузера Storm)'
    });
  }

  const results = {};
  const tasks = ids.map((workitemId) => async () => {
    const history = await fetchHistoryForWorkitem(workspaceId, workitemId, auth);
    if (!history) {
      results[workitemId] = { fact: 0, factQA: 0, handoffFact: 0 };
      return;
    }
    const filtered = history.filter((e) => {
      const t = (e.type || '').toLowerCase();
      return t === 'statusupdated' || t === 'workitemstatusupdated';
    });
    const fact = Math.round(calculateInProgressMinutes(filtered) * 10) / 10;
    const factQA = Math.round(calculateTestingMinutes(filtered) * 10) / 10;
    const handoffFact = Math.round(calculateInAssessmentTestingMinutes(filtered) * 10) / 10;
    results[workitemId] = { fact, factQA, handoffFact };
  });

  await runWithConcurrency(tasks, 8);
  res.json(results);
});

// GET workitems/:id/history/ready-to-test — пары (попала в тесты, взяли в тест, лаг)
app.get('/api/workspaces/:workspaceId/workitems/:workitemId/history/ready-to-test', async (req, res) => {
  const sessionToken = process.env.STORM_SESSION_TOKEN;
  const apiToken = (process.env.STORM_API_TOKEN || '').trim();
  const { workspaceId, workitemId } = req.params;
  const auth = sessionToken
    ? { headers: { 'Cookie': `session=${sessionToken}` } }
    : apiToken
      ? { headers: { 'Authorization': `PrivateToken ${apiToken}` } }
      : null;
  if (!auth) {
    return res.status(500).json({
      error: 'STORM_SESSION_TOKEN or STORM_API_TOKEN required',
      hint: 'History API требует сессию'
    });
  }
  try {
    const history = await fetchHistoryForWorkitem(workspaceId, workitemId, auth);
    if (!history) {
      return res.json({ pairs: [] });
    }
    const filtered = history.filter((e) => {
      const t = (e.type || '').toLowerCase();
      return t === 'statusupdated' || t === 'workitemstatusupdated';
    });
    const pairs = computeReadyToTestPairs(filtered);
    res.json({ pairs });
  } catch (err) {
    console.error('History ready-to-test failed:', err.message);
    res.status(502).json({ error: 'Failed to fetch history', details: err.message });
  }
});

app.get('/api/workspaces/:workspaceId/folders/:folderId/workitems', async (req, res) => {
  const apiToken = (process.env.STORM_API_TOKEN || '').trim();
  const { workspaceId, folderId } = req.params;
  const { maxItemsCount, fromToken } = req.query;

  if (!apiToken) {
    return res.status(500).json({
      error: 'STORM_API_TOKEN not configured',
      hint: 'Добавьте токен в .env для доступа к задачам'
    });
  }

  const params = new URLSearchParams();
  params.set('folderId', folderId);
  if (maxItemsCount) params.set('maxItemsCount', maxItemsCount);
  if (fromToken) params.set('fromToken', fromToken);
  if (req.query.sprintId) params.set('sprintId', req.query.sprintId);

  const url = `https://storm.alabuga.space/cwm/public/api/v1/workspaces/${encodeURIComponent(workspaceId)}/workitems?${params}`;

  try {
    const response = await fetch(url, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `PrivateToken ${apiToken}`
      }
    });

    const text = await response.text();
    if (!response.ok) {
      let details;
      try {
        const errJson = JSON.parse(text);
        details = errJson.messages?.join('; ') || errJson.key || text.slice(0, 300);
      } catch {
        details = text.slice(0, 300);
      }
      return res.status(response.status).json({
        error: 'Storm workitems API error',
        status: response.status,
        details,
        hint: response.status === 401 ? 'Обновите STORM_API_TOKEN в .env' : null
      });
    }

    let data;
    try {
      data = JSON.parse(text);
    } catch {
      return res.status(502).json({
        error: 'Storm вернул некорректный ответ',
        hint: 'Обновите STORM_API_TOKEN в .env'
      });
    }
    res.json(data);
  } catch (err) {
    console.error('Workitems API failed:', err.message);
    res.status(502).json({ error: 'Failed to fetch workitems', details: err.message });
  }
});

// PATCH/PUT workitem — обновление originalEstimate (поле "Время" в карточке задачи, секунды)
async function handleWorkitemUpdate(req, res) {
  const apiToken = (process.env.STORM_API_TOKEN || '').trim();
  const { workspaceId, workitemId } = req.params;
  const body = req.body || {};
  const raw = body.originalEstimate ?? body.estimatedTime;
  const seconds = typeof raw === 'number' ? raw : (typeof raw === 'string' ? parseFloat(raw) : NaN);
  const folderId = req.query.folderId;

  if (!apiToken) {
    return res.status(500).json({ error: 'STORM_API_TOKEN not configured' });
  }

  if (Number.isNaN(seconds) || seconds < 0) {
    return res.status(400).json({ error: 'originalEstimate must be a non-negative number (seconds)' });
  }

  const base = 'https://storm.alabuga.space/cwm/public/api/v1';
  const estimatedSeconds = Math.round(seconds);
  const headers = {
    'Content-Type': 'application/json',
    'Authorization': `PrivateToken ${apiToken}`
  };

  // Storm API: originalEstimate = поле "Время" (оценка) в секундах
  const payload = { originalEstimate: estimatedSeconds };
  const urlCandidates = [
    `${base}/workspaces/${encodeURIComponent(workspaceId)}/workitems/${encodeURIComponent(workitemId)}`,
    `${base}/workspaces/${encodeURIComponent(workspaceId)}/nodes/${encodeURIComponent(workitemId)}`
  ];
  if (folderId) {
    urlCandidates.unshift(`${base}/workspaces/${encodeURIComponent(workspaceId)}/folders/${encodeURIComponent(folderId)}/workitems/${encodeURIComponent(workitemId)}`);
  }

  let lastError = null;
  const methods = ['PATCH', 'PUT'];
  for (const url of urlCandidates) {
    for (const method of methods) {
      try {
        const response = await fetch(url, {
          method,
          headers,
          body: JSON.stringify(payload)
        });

        const text = await response.text();
        console.log('[Storm]', method, response.status, url, '→', text.slice(0, 200));
        if (response.ok) {
          let data = null;
          try { data = text ? JSON.parse(text) : {}; } catch { /* empty ok */ }
          return res.json(data || { ok: true });
        }
        lastError = { status: response.status, text, url, method };
        if (response.status !== 404) break;
      } catch (err) {
        lastError = { err: err.message, url };
        break;
      }
    }
    if (lastError && lastError.err) break;
  }

  const urls = urlCandidates;

  let details = 'API не найден (404). Проверьте документацию Storm.';
  const status = lastError.status || 502;
  if (lastError.text) {
    try {
      const errJson = JSON.parse(lastError.text);
      details = errJson.messages?.join('; ') || errJson.error || errJson.key || lastError.text.slice(0, 400);
    } catch {
      details = lastError.text.slice(0, 400);
    }
  } else if (lastError.err) {
    details = lastError.err;
  }
  return res.status(status).json({
    error: 'Storm workitem update failed',
    status,
    details,
    triedUrls: urls,
    sentPayload: { originalEstimate: estimatedSeconds },
    hint: status === 401
      ? 'Обновите STORM_API_TOKEN в .env'
      : 'Storm API не поддерживает PATCH/PUT workitems по этим путям. Поле originalEstimate (сек) = «Время» в карточке задачи.'
  });
}
app.patch('/api/workspaces/:workspaceId/workitems/:workitemId', handleWorkitemUpdate);
app.put('/api/workspaces/:workspaceId/workitems/:workitemId', handleWorkitemUpdate);

// Список спринтов (Agile) пространства или конкретной папки
app.get('/api/workspaces/:workspaceId/sprints', async (req, res) => {
  const apiToken = (process.env.STORM_API_TOKEN || '').trim();
  const { workspaceId } = req.params;
  const { maxItemsCount, folderId } = req.query;

  if (!apiToken) {
    return res.status(500).json({ error: 'STORM_API_TOKEN not configured' });
  }

  const params = new URLSearchParams();
  if (maxItemsCount) params.set('maxItemsCount', Math.min(100, maxItemsCount));
  if (folderId) params.set('folderId', folderId);
  const url = `https://storm.alabuga.space/cwm/public/api/v1/workspaces/${encodeURIComponent(workspaceId)}/sprints${params.toString() ? '?' + params : ''}`;

  try {
    const response = await fetch(url, {
      headers: { 'Content-Type': 'application/json', 'Authorization': `PrivateToken ${apiToken}` }
    });
    const text = await response.text();
    if (!response.ok) {
      let details;
      try {
        const errJson = JSON.parse(text);
        details = errJson.messages?.join('; ') || errJson.key || text.slice(0, 300);
      } catch { details = text.slice(0, 300); }
      return res.status(response.status).json({ error: 'Storm sprints API error', status: response.status, details });
    }
    let data;
    try { data = JSON.parse(text); } catch { return res.status(502).json({ error: 'Некорректный ответ Storm' }); }
    res.json(data);
  } catch (err) {
    console.error('Sprints API failed:', err.message);
    res.status(502).json({ error: 'Failed to fetch sprints', details: err.message });
  }
});

app.get('/api/workspaces/:workspaceId/folders', async (req, res) => {
  const sessionToken = process.env.STORM_SESSION_TOKEN;
  const { workspaceId } = req.params;
  const { name, parentId, maxItemsCount } = req.query;

  if (!sessionToken) {
    return res.status(500).json({
      error: 'Session token not configured',
      hint: 'Create .env file with STORM_SESSION_TOKEN=your_session_cookie_value'
    });
  }

  const params = new URLSearchParams();
  if (name) params.set('name', name);
  if (parentId) params.set('parentId', parentId);
  if (maxItemsCount) params.set('maxItemsCount', maxItemsCount);
  const qs = params.toString();

  const publicUrl = `https://storm.alabuga.space/cwm/public/api/v1/workspaces/${encodeURIComponent(workspaceId)}/folders${qs ? '?' + qs : ''}`;
  const apiToken = (process.env.STORM_API_TOKEN || '').trim();

  const headers = {
    'Content-Type': 'application/json',
    ...(apiToken ? { 'Authorization': `PrivateToken ${apiToken}` } : { 'Cookie': `session=${sessionToken}` })
  };

  try {
    const response = await fetch(publicUrl, {
      method: 'GET',
      headers
    });

    const text = await response.text();
    if (!response.ok) {
      const hint = response.status === 401
        ? (!apiToken
          ? 'Добавьте STORM_API_TOKEN в .env (Профиль Storm → Создание токена). Передаётся как PrivateToken.'
          : 'Токен неверный или истёк. Создайте новый в Профиле Storm и обновите .env')
        : null;
      return res.status(response.status).json({
        error: 'Storm folders API error',
        status: response.status,
        details: text.slice(0, 500),
        hint
      });
    }

    let data;
    try {
      data = JSON.parse(text);
    } catch {
      return res.status(502).json({
        error: 'Storm вернул некорректный ответ (HTML вместо JSON)',
        hint: 'Сессия истекла или требуется API-токен. Добавьте STORM_API_TOKEN в .env'
      });
    }
    res.json(data);
  } catch (err) {
    console.error('Storm folders API request failed:', err.message);
    res.status(502).json({
      error: 'Failed to fetch folders',
      details: err.message
    });
  }
});

app.get('/api/workspaces', async (req, res) => {
  const sessionToken = process.env.STORM_SESSION_TOKEN;
  
  if (!sessionToken) {
    return res.status(500).json({
      error: 'Session token not configured',
      hint: 'Create .env file with STORM_SESSION_TOKEN=your_session_cookie_value'
    });
  }

  try {
    const response = await fetch(STORM_API, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Cookie': `session=${sessionToken}`
      }
    });

    const text = await response.text();
    if (!response.ok) {
      return res.status(response.status).json({
        error: 'Storm API error',
        status: response.status,
        details: text.slice(0, 500)
      });
    }

    let data;
    try {
      data = JSON.parse(text);
    } catch {
      return res.status(502).json({
        error: 'Storm вернул некорректный ответ (HTML вместо JSON)',
        hint: 'Сессия истекла? Обновите STORM_SESSION_TOKEN в .env'
      });
    }
    res.json(data);
  } catch (err) {
    console.error('Storm API request failed:', err.message);
    res.status(502).json({
      error: 'Failed to fetch workspaces',
      details: err.message
    });
  }
});

app.use(express.static(path.join(__dirname, 'public')));

// 404 для /api/* — чтобы не отдавать index.html
app.use('/api', (req, res) => {
  res.status(404).json({ error: 'Not found', path: req.path });
});

app.listen(PORT, () => {
  console.log(`Server: http://localhost:${PORT}`);
  if (!process.env.STORM_SESSION_TOKEN) console.warn('Warning: STORM_SESSION_TOKEN not set.');
  if (!(process.env.STORM_API_TOKEN || '').trim()) console.warn('Warning: STORM_API_TOKEN not set — папки будут недоступны.');
});
