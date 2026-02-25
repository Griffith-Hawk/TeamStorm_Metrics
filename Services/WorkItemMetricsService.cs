using TeamStorm.Metrics.Models;

namespace TeamStorm.Metrics.Services;

public sealed class WorkItemMetricsService : IWorkItemMetricsService
{
    private static readonly TimeOnly WorkStart = new(8, 0);
    private static readonly TimeOnly WorkEnd = new(17, 0);
    private static readonly TimeZoneInfo MoscowTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    // Основная бизнес-логика: считаем минуты в статусе in progress только в рабочее время.
    public double CalculateFactMinutes(IReadOnlyList<HistoryEventDto> history, DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        var ordered = history
            .Where(x => x.Date.HasValue)
            .Select(x => new
            {
                x.Date,
                Status = NormalizeStatus(x.Data?.NewValue?.StatusName ?? x.Data?.NewValue?.Name)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Status))
            .OrderBy(x => x.Date)
            .ToList();

        var inProgress = false;
        var lastPoint = periodStart;
        var totalMinutes = 0d;

        foreach (var evt in ordered)
        {
            if (evt.Date <= periodStart)
            {
                inProgress = evt.Status == "in progress";
                continue;
            }

            if (evt.Date > periodEnd)
            {
                break;
            }

            if (inProgress)
            {
                totalMinutes += WorkingMinutes(lastPoint, evt.Date!.Value);
            }

            inProgress = evt.Status == "in progress";
            lastPoint = evt.Date!.Value;
        }

        if (inProgress && lastPoint < periodEnd)
        {
            totalMinutes += WorkingMinutes(lastPoint, periodEnd);
        }

        return Math.Round(totalMinutes, 2);
    }

    private static string NormalizeStatus(string? source)
    {
        return (source ?? string.Empty).Trim().ToLowerInvariant().Replace('о', 'o');
    }

    private static double WorkingMinutes(DateTimeOffset from, DateTimeOffset to)
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

            if (segEnd > segStart)
            {
                minutes += (segEnd - segStart).TotalMinutes;
            }

            cursor = cursor.Date.AddDays(1).Add(WorkStart.ToTimeSpan());
        }

        return minutes;
    }
}
