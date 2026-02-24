using TeamStorm.Metrics.Models;

namespace TeamStorm.Metrics.Services;

public interface IWorkItemMetricsService
{
    double CalculateFactMinutes(IReadOnlyList<HistoryEventDto> history, DateTimeOffset periodStart, DateTimeOffset periodEnd);
}
