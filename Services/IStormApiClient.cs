using System.Text.Json;

namespace TeamStorm.Metrics.Services;

public interface IStormApiClient
{
    Task<JsonElement> PostAsync(string relativePath, object? payload, CancellationToken cancellationToken);
    Task<JsonElement> GetAsync(string relativePath, CancellationToken cancellationToken);
    Task<JsonElement> SendWorkItemUpdateAsync(string workspaceId, string workitemId, int originalEstimateSeconds, string? folderId, CancellationToken cancellationToken);
}
