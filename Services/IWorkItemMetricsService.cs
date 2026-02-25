using TeamStorm.Metrics.Models;

namespace TeamStorm.Metrics.Services;

public interface IWorkItemMetricsService
{
    double CalculateInProgressMinutes(IReadOnlyList<HistoryEventDto> history);
    double CalculateTestingMinutes(IReadOnlyList<HistoryEventDto> history);
    double CalculateInAssessmentTestingMinutes(IReadOnlyList<HistoryEventDto> history);
    IReadOnlyList<ReadyToTestPairDto> ComputeReadyToTestPairs(IReadOnlyList<HistoryEventDto> history);
}
