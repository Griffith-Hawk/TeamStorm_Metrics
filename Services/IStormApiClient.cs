using TeamStorm.Metrics.Models;

namespace TeamStorm.Metrics.Services;

public interface IStormApiClient
{
    Task<IReadOnlyList<WorkspaceDto>> GetWorkspacesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FolderDto>> GetFoldersAsync(string workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(string workspaceId, string folderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HistoryEventDto>> GetWorkItemHistoryAsync(string workspaceId, string workItemId, CancellationToken cancellationToken);
}
