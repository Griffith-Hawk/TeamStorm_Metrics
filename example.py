""" Подсчет времени разработки по нескольким периодам в один Excel (по листу на период) """

import requests
import json
from datetime import datetime, timedelta
from pytz import timezone
import xlsxwriter
from collections import defaultdict
from typing import List, Tuple, Dict, Any, Optional


# === Настройки ===
MOSCOW_TZ = timezone("Europe/Moscow")
WORK_START_HOUR = 8
WORK_END_HOUR = 17
GLOBAL_IN_PROGRESS_SETTING = "in progress"


def _normalize_status_for_in_progress(s: str) -> str:
    """Нормализует статус: заменяет все варианты 'о' на латинскую (in prоgress → in progress)."""
    if not s or not isinstance(s, str):
        return ""
    # Кириллица о/О, греческая ο, Cyrillic ө/ӫ, fullwidth ｏ и др.
    o_like = (
        "\u043e", "\u041e",  # Cyrillic о, О
        "\u03bf",            # Greek omicron ο
        "\u04e9", "\u04eb",  # Cyrillic ө, ӫ
        "\uff4f",            # Fullwidth ｏ
        "\u1d0f", "\u0275",  # ǒ, ɵ
    )
    t = s
    for c in o_like:
        t = t.replace(c, "o")
    return " ".join(t.split()).strip().lower()


def _is_in_progress_status(status: str) -> bool:
    return _normalize_status_for_in_progress(status) == GLOBAL_IN_PROGRESS_SETTING

# Исключенные периоды (даты включительно)
EXCLUDED_PERIODS = [
    ("2025-12-31", "2026-01-11"),
    # Можно добавить другие периоды здесь
]


def parse_iso_to_msk(dt_str: str) -> datetime:
    """Парсит ISO-дату из истории (с 'Z') и переводит в МСК (timezone-aware)."""
    return datetime.fromisoformat(dt_str.replace("Z", "+00:00")).astimezone(MOSCOW_TZ)


def clamp_to_workday_window(dt: datetime) -> datetime:
    """Смещает время в границы рабочего окна [08:00, 17:00] того же дня (если нужно)."""
    day_start = dt.replace(hour=WORK_START_HOUR, minute=0, second=0, microsecond=0)
    day_end = dt.replace(hour=WORK_END_HOUR, minute=0, second=0, microsecond=0)
    if dt < day_start:
        return day_start
    if dt > day_end:
        return day_end
    return dt


def is_working_day(d: datetime) -> bool:
    """Пн–Пт → True; Сб(5), Вс(6) → False"""
    return d.weekday() not in (5, 6)


def is_date_in_excluded_period(date: datetime) -> bool:
    """Проверяет, попадает ли дата в исключенный период."""
    date_only = date.date()
    for start_str, end_str in EXCLUDED_PERIODS:
        start_date = datetime.strptime(start_str, "%Y-%m-%d").date()
        end_date = datetime.strptime(end_str, "%Y-%m-%d").date()
        if start_date <= date_only <= end_date:
            return True
    return False


def add_working_time_segment(start_dt: datetime, end_dt: datetime) -> timedelta:
    """
    Возвращает длительность пересечения [start_dt, end_dt] с рабочими окнами
    (Пн–Пт, 08:00–17:00 МСК), исключая заданные периоды.
    """
    if end_dt <= start_dt:
        return timedelta(0)

    total = timedelta(0)
    cur = start_dt

    # Итерация по дням
    while cur.date() <= end_dt.date():
        # Пропускаем исключенные периоды
        if is_date_in_excluded_period(cur):
            # Перепрыгиваем на следующий день после конца исключенного периода
            # Находим конец текущего исключенного периода
            cur_date = cur.date()
            for start_str, end_str in EXCLUDED_PERIODS:
                start_date = datetime.strptime(start_str, "%Y-%m-%d").date()
                end_date = datetime.strptime(end_str, "%Y-%m-%d").date()
                if start_date <= cur_date <= end_date:
                    # Переходим на день после конца исключенного периода
                    cur = (datetime.combine(end_date, datetime.min.time()) + timedelta(days=1)).replace(
                        hour=WORK_START_HOUR, minute=0, second=0, microsecond=0, tzinfo=MOSCOW_TZ
                    )
                    break
            else:
                # Если не попали ни в один исключенный период, просто переходим на следующий день
                cur = (cur + timedelta(days=1)).replace(hour=WORK_START_HOUR, minute=0, second=0, microsecond=0)
            continue

        if not is_working_day(cur):
            # Перепрыгиваем на следующий рабочий день к 08:00
            cur = (cur + timedelta(days=1)).replace(hour=WORK_START_HOUR, minute=0, second=0, microsecond=0)
            continue

        day_start = cur.replace(hour=WORK_START_HOUR, minute=0, second=0, microsecond=0)
        day_end = cur.replace(hour=WORK_END_HOUR, minute=0, second=0, microsecond=0)

        # Отрезок дня, который нам нужен
        seg_start = max(cur, day_start)
        seg_end = min(end_dt, day_end)

        if seg_start < seg_end:
            total += (seg_end - seg_start)

        # Переходим на следующий день, к 08:00
        cur = (cur + timedelta(days=1)).replace(hour=WORK_START_HOUR, minute=0, second=0, microsecond=0)

    return total


def calculate_in_progress_time_for_period(
    history: List[Dict[str, Any]],
    period_start_str: str,
    period_end_str: str
) -> float:
    """
    Вычисляет время (в часах) в статусе 'in progress' ТОЛЬКО внутри заданного периода,
    учитывая рабочие часы и исключая выходные.

    Важно: корректно учитывает случаи, когда статус 'in progress' начался
    до начала периода или закончился после конца периода.
    """
    # Период в МСК
    period_start = datetime.strptime(period_start_str, "%Y-%m-%d").replace(tzinfo=MOSCOW_TZ, hour=0, minute=0, second=0, microsecond=0)
    period_end = datetime.strptime(period_end_str, "%Y-%m-%d").replace(tzinfo=MOSCOW_TZ, hour=23, minute=59, second=59, microsecond=0)

    # История: статус в entry['data']['newValue']['statusName'] или ['name']
    events = []
    for e in history:
        try:
            nv = e.get("data", {}).get("newValue") or {}
            raw = nv.get("statusName") or nv.get("name") or (nv if isinstance(nv, str) else "") or ""
            new_status = _normalize_status_for_in_progress(raw) or raw
            if not e.get("date") or not new_status:
                continue
            events.append((parse_iso_to_msk(e["date"]), new_status))
        except Exception:
            # Пропускаем кривые записи
            continue

    if not events:
        return 0.0

    events.sort(key=lambda x: x[0])

    # Определяем состояние на момент period_start
    in_progress = False
    for dt, status in events:
        if dt <= period_start:
            in_progress = _is_in_progress_status(status)
        else:
            break

    # Бежим по событиям в пределах до period_end
    last_ts = period_start
    total_td = timedelta(0)

    for dt, status in events:
        if dt <= period_start:
            # Уже учтено выше только для определения текущего состояния
            continue

        if dt > period_end:
            # Доехали до конца периода — добавим хвост если нужно и выйдем
            if in_progress:
                total_td += add_working_time_segment(last_ts, period_end)
            break

        # От last_ts до dt — состояние инвариантное
        if in_progress:
            total_td += add_working_time_segment(last_ts, dt)

        # Обновляем состояние и маркер времени
        in_progress = _is_in_progress_status(status)
        last_ts = dt

    else:
        # Если цикл завершился без break и период не закрыт событиями
        if last_ts < period_end and in_progress:
            total_td += add_working_time_segment(last_ts, period_end)

    # hours = total_td.total_seconds() / 3600.0
    minutes = total_td.total_seconds() / 60
    return minutes  # часы с дробью (без округления до часа)


def save_to_excel_multi(grouped_by_period: Dict[Tuple[str, str], Dict[str, List[List[Any]]]], filename: str) -> None:
    workbook = xlsxwriter.Workbook(filename)

    for (start_str, end_str), grouped_data in grouped_by_period.items():
        sheet_name = f"{start_str}_{end_str}"
        if len(sheet_name) > 31:
            sheet_name = sheet_name[:31]

        worksheet = workbook.add_worksheet(sheet_name)

        headers = ["Display Name", "Task Key", "Task Name", "In Progress Hours", "Days", "Tasks Count"]
        for col_num, header in enumerate(headers):
            worksheet.write(0, col_num, header)

        row = 1
        for display_name, tasks in grouped_data.items():
            if not tasks:
                continue

            tasks = sorted(tasks, key=lambda x: x[0])

            # Корректируем отображение часов для задачи: <1 часа → 1
            display_hours_list = [task[2] if task[2] >= 1 else 1 for task in tasks]
            total_hours = sum(display_hours_list)
            total_days = round(total_hours / 8, 1)
            tasks_count = len(tasks)

            if len(tasks) > 1:
                worksheet.merge_range(row, 0, row + len(tasks) - 1, 0, display_name)
                worksheet.merge_range(row, 4, row + len(tasks) - 1, 4, total_days)
                worksheet.merge_range(row, 5, row + len(tasks) - 1, 5, tasks_count)
            else:
                worksheet.write(row, 0, display_name)
                worksheet.write(row, 4, total_days)
                worksheet.write(row, 5, tasks_count)

            for i, task in enumerate(tasks):
                task_key, task_name, hours = task
                display_hours = display_hours_list[i]
                worksheet.write(row, 1, task_key)
                worksheet.write(row, 2, task_name)
                worksheet.write(row, 3, display_hours)
                row += 1

    workbook.close()
    print(f"Данные успешно сохранены в файл {filename}")


def fetch_and_save_data(
    items: List[Dict[str, Any]],
    periods: List[Tuple[str, str]],
    filename: str = "bchf.xlsx",
    session_cookie: Optional[str] = None
) -> None:
    """
    Загружает историю для items один раз, затем считает время по каждому периоду
    и пишет в разные листы одного Excel.
    """
    if not periods:
        print("Список periods пуст.")
        return

    base_url = "https://storm.alabuga.space/history/api/v1"
    grouped_by_period: Dict[Tuple[str, str], Dict[str, List[List[Any]]]] = {
        (p[0], p[1]): defaultdict(list) for p in periods
    }

    cookies = {}
    if session_cookie:
        cookies = {"session": session_cookie}

    for item in items:
        key = item.get("key")
        workspace_id = item.get("workspaceId")
        workitem_id = item.get("workitemId")
        if not key or not workspace_id or not workitem_id:
            print(f"Пропущен элемент из-за отсутствия ключевых данных: {item}")
            continue

        # 1) Тянем историю один раз
        history_url = f"{base_url}/workspaces/{workspace_id}/workItems/{workitem_id}/history"
        try:
            resp = requests.get(history_url, cookies=cookies)
            resp.raise_for_status()
            history_data = resp.json()

            # Только события смены статуса
            filtered_history = [
                {
                    "date": entry.get("date"),
                    "type": entry.get("type"),
                    "data": entry.get("data"),
                }
                for entry in history_data
                if entry.get("type") == "StatusUpdated"
            ]

            # Данные задачи
            assignee = item.get("assignee", {}) or {}
            display_name = assignee.get("displayName", "Не указано")
            task_name = item.get("name", "Не указано")

            # 2) Для каждого периода считаем часы отдельно
            for (start_str, end_str) in periods:
                mins = calculate_in_progress_time_for_period(filtered_history, start_str, end_str)

                # Если время > 0 минут, записываем задачу
                # Если значение меньше часа, округляем до 1 часа
                if mins > 0:
                    hours = round(mins / 60, 1)
                    if hours < 1.0:
                        hours = 1.0
                    grouped_by_period[(start_str, end_str)][display_name].append([key, task_name, hours])

            print(f"История обработана для key={key}")

        except requests.RequestException as e:
            print(f"Ошибка при запросе {history_url}: {e}")
            continue

    # 3) Сохранение в Excel
    save_to_excel_multi(grouped_by_period, filename)


# === Пример использования как скрипта ===
if __name__ == "__main__":
    # 1) Загружаем items из файла
    try:
        with open("example_table_release.json", "r", encoding="utf-8") as f:
            data = json.load(f)
            items = data.get("items", [])
    except (FileNotFoundError, json.JSONDecodeError) as e:
        print(f"Ошибка при чтении example_table_release.json: {e}")
        items = []

    # 2) Задаём периоды
    periods = [
        ("2025-12-22", "2025-12-24"),
        ("2025-12-25", "2026-01-23"),
        ("2025-12-22", "2026-01-23"),
    ]

    # 3) Имя файла и session cookie
    FILENAME = "BC.xlsx"
    SESSION_COOKIE = "CfDJ8L_swNlPsvBBlXgvsIHNQNcOrVBPAy2mYTyiiYUuLD_cJvZpr3E-Sc2d2WPxHcYGVf1Ew4HwckWrygJe6Ulu-wxPB6DTCB_uJe8DsqF1AGQgG5KO1etinbO-PARmAWCXVX8mJWrAwUunHG6xlfdJyi4wHFqgqkZW288icj3IvQuq5TZhCat97J55l5ykqQKgy7q7CcyZpIYAZuqaykuSv8W4bxExrtXuXnrRvn7JFBADQYk8mVdgMMD70qXutlgo4xxU_iVMXH-TdOuO7_qK7waOVI0ga1b9f-KlWyhj1FHFRrT_kz7dT-pLSJKGtghrLIcN39oXjOQvABUzkzh_I6HCNNFV9wriLlDTk3Zd2auCzXqMeInA0j_IsAByCeQxc95UL6AzpnlzLwUfpvU8xVDL0Yh12CkJVGjcD5QDwjjoVF8fAlqM2W-KlTHnu9mqzo09L7ik0xKpFBVjL3fzahzBry9fpmirYvAebFrecmIDbJnSLP2xUc10EwgrHyE3QAO3BYXvNTR55pmI6x78_5lON7kXBWpJPMaSofd4YhU2qsymTvuiWtgh-nltUO1EQ55Pd6eNfLRmeE4PFs2bYFalrIjnAyivhMmc2oa3K8f74UeeZowokjl-htUAYB4De-RMjx8RKmJhbEXxjHBPAbk3G04TwDBmWkS69QVQljw-IibOdrSPHqVyD5ghnpPJ8Hr4g6Kbn1jxGp0yUjspZHwVzYv3NT1vLAas2oi9RnuIKJBq1uPG9N7PdW5NjYKWXyQ7VeQluCAi4ICRtPdqkMgu_UNri_9G-GaDvRAYmZnTT7C6F4PBfyK8KoTyh0GWfPZHf1gIdJ2bh6eA7-gAT1y2fO7VCw-IAdmFWFbft_7CCwCicH95Zt0mTovJFcAP7voisZcQlEXLdaBOg9jd6TA"

    # 4) Запуск
    fetch_and_save_data(items, periods=periods, filename=FILENAME, session_cookie=SESSION_COOKIE)