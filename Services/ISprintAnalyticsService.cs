using TeamStorm.Metrics.Models;

namespace TeamStorm.Metrics.Services;

public interface ISprintAnalyticsService
{
    Task<ProjectAnalyticsDto> GetProjectAnalyticsAsync(string workspaceId, string? workspaceName, CancellationToken cancellationToken);
    Task<SprintAnalyticsDto> GetSprintAnalyticsAsync(string workspaceId, string folderId, string sprintId, CancellationToken cancellationToken);
}
