using System.Text;
using TeamStorm.Metrics.Models;

namespace TeamStorm.Metrics.Services;

public sealed class WorkItemMetricsService : IWorkItemMetricsService
{
    private static readonly TimeOnly WorkStart = new(8, 0);
    private static readonly TimeOnly WorkEnd = new(17, 0);
    private static readonly TimeZoneInfo MoscowTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    public double CalculateInProgressMinutes(IReadOnlyList<HistoryEventDto> history)
        => CalculateTimeInStatusMinutes(history, IsInProgressStatus, WorkingMinutesBetween);

    public double CalculateTestingMinutes(IReadOnlyList<HistoryEventDto> history)
        => CalculateTimeInStatusMinutes(history, IsTestingStatus, FullDayMinutesBetween);

    public double CalculateInAssessmentTestingMinutes(IReadOnlyList<HistoryEventDto> history)
        => CalculateTimeInStatusMinutes(history, IsAssessmentTestingStatus, FullDayMinutesBetween);

    // Комментарий: пары нужны для UI-аналитики QA (попала в тесты -> взяли в тест).
    public IReadOnlyList<ReadyToTestPairDto> ComputeReadyToTestPairs(IReadOnlyList<HistoryEventDto> history)
    {
        var events = StatusEvents(history).ToList();
        var pairs = new List<ReadyToTestPairDto>();
        DateTimeOffset? lastReadyAt = null;
        HistoryEventDto? lastReadyEvent = null;

        for (var i = 0; i < events.Count; i++)
        {
            var (evt, status) = events[i];
            var dt = evt.Date!.Value;

            if (IsReadyToTestOrDev(status))
            {
                if (lastReadyAt.HasValue)
                {
                    pairs.Add(new ReadyToTestPairDto { ReadyAt = lastReadyAt.Value.ToString("O"), ReadyBy = GetEventUser(lastReadyEvent), TakenAt = null, TakenBy = null, LagHours = null, TestingHours = null });
                }
                lastReadyAt = dt;
                lastReadyEvent = evt;
            }
            else if (IsTestingStatus(status))
            {
                var readyAt = lastReadyAt ?? (i > 0 ? events[i - 1].Item1.Date ?? dt : dt);
                var lag = Math.Max(0, (dt - readyAt).TotalHours);
                var nextDt = i + 1 < events.Count ? events[i + 1].Item1.Date ?? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow;
                var testing = Math.Max(0, (nextDt - dt).TotalHours);

                pairs.Add(new ReadyToTestPairDto
                {
                    ReadyAt = readyAt.ToString("O"),
                    ReadyBy = GetEventUser(lastReadyAt.HasValue ? lastReadyEvent : (i > 0 ? events[i - 1].Item1 : null)),
                    TakenAt = dt.ToString("O"),
                    TakenBy = GetEventUser(evt),
                    LagHours = Math.Round(lag, 1),
                    TestingHours = Math.Round(testing, 1)
                });

                lastReadyAt = null;
                lastReadyEvent = null;
            }
            else if (lastReadyAt.HasValue)
            {
                pairs.Add(new ReadyToTestPairDto { ReadyAt = lastReadyAt.Value.ToString("O"), ReadyBy = GetEventUser(lastReadyEvent), TakenAt = null, TakenBy = null, LagHours = null, TestingHours = null });
                lastReadyAt = null;
                lastReadyEvent = null;
            }
        }

        if (lastReadyAt.HasValue)
        {
            pairs.Add(new ReadyToTestPairDto { ReadyAt = lastReadyAt.Value.ToString("O"), ReadyBy = GetEventUser(lastReadyEvent), TakenAt = null, TakenBy = null, LagHours = null, TestingHours = null });
        }

        return pairs;
    }

    private static double CalculateTimeInStatusMinutes(
        IReadOnlyList<HistoryEventDto> history,
        Func<string, bool> statusPredicate,
        Func<DateTimeOffset, DateTimeOffset, double> segmentCalculator)
    {
        var events = StatusEvents(history).ToList();
        if (events.Count == 0) return 0;

        var periodStart = events[0].Item1.Date!.Value;
        var periodEnd = DateTimeOffset.UtcNow;
        var inTarget = statusPredicate(events[0].Item2);
        var lastTs = periodStart;
        var total = 0d;

        foreach (var (evt, status) in events.Skip(1))
        {
            var dt = evt.Date!.Value;
            if (dt > periodEnd) break;

            if (inTarget)
            {
                total += segmentCalculator(lastTs, dt);
            }

            inTarget = statusPredicate(status);
            lastTs = dt;
        }

        if (inTarget && lastTs < periodEnd)
        {
            total += segmentCalculator(lastTs, periodEnd);
        }

        return total;
    }

    private static IEnumerable<(HistoryEventDto, string)> StatusEvents(IReadOnlyList<HistoryEventDto> history)
        => history
            .Where(x => x.Date.HasValue)
            .Where(x => x.Type?.Equals("StatusUpdated", StringComparison.OrdinalIgnoreCase) == true
                     || x.Type?.Equals("WorkItemStatusUpdated", StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => (x, NormalizeStatus(x.Data?.NewValue?.StatusName ?? x.Data?.NewValue?.Name)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item2))
            .OrderBy(x => x.Item1.Date);

    private static string NormalizeStatus(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        var sb = new StringBuilder(source.Trim().ToLowerInvariant());
        for (var i = 0; i < sb.Length; i++)
        {
            if (sb[i] == 'о') sb[i] = 'o';
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsInProgressStatus(string status) => status == "in progress";
    private static bool IsTestingStatus(string status) => status == "testing";
    private static bool IsAssessmentTestingStatus(string status) => status == "in assessment testing";
    private static bool IsReadyToTestOrDev(string status) => status is "ready to test" or "dev" or "dev waiting";

    private static string? GetEventUser(HistoryEventDto? evt)
    {
        if (evt is null) return null;
        if (TryReadDisplayName(evt.User, out var user)) return user;
        if (TryReadDisplayName(evt.Author, out var author)) return author;
        if (TryReadDisplayName(evt.Data?.User, out var dataUser)) return dataUser;
        if (TryReadDisplayName(evt.Data?.Author, out var dataAuthor)) return dataAuthor;
        return evt.UserId ?? evt.AuthorId ?? evt.Data?.UserId;
    }

    private static bool TryReadDisplayName(System.Text.Json.JsonElement? element, out string? value)
    {
        value = null;
        if (element is null || element.Value.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        if (element.Value.TryGetProperty("displayName", out var dn)) { value = dn.GetString(); return !string.IsNullOrWhiteSpace(value); }
        if (element.Value.TryGetProperty("name", out var n)) { value = n.GetString(); return !string.IsNullOrWhiteSpace(value); }
        return false;
    }

    private static double FullDayMinutesBetween(DateTimeOffset from, DateTimeOffset to)
        => to <= from ? 0 : (to - from).TotalMinutes;

    private static double WorkingMinutesBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) return 0;

        var cursor = TimeZoneInfo.ConvertTime(from, MoscowTz);
        var finish = TimeZoneInfo.ConvertTime(to, MoscowTz);
        var minutes = 0d;

        while (cursor < finish)
        {
            if (cursor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                cursor = cursor.Date.AddDays(1).Add(WorkStart.ToTimeSpan());
                continue;
            }

            var dayStart = cursor.Date.Add(WorkStart.ToTimeSpan());
            var dayEnd = cursor.Date.Add(WorkEnd.ToTimeSpan());
            var segStart = cursor < dayStart ? dayStart : cursor;
            var segEnd = finish < dayEnd ? finish : dayEnd;

            if (segEnd > segStart) minutes += (segEnd - segStart).TotalMinutes;
            cursor = cursor.Date.AddDays(1).Add(WorkStart.ToTimeSpan());
        }

        return minutes;
    }
}
