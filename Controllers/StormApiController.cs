using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TeamStorm.Metrics.Models;
using TeamStorm.Metrics.Services;

namespace TeamStorm.Metrics.Controllers;

[ApiController]
[Route("api")]
public sealed class StormApiController : ControllerBase
{
    private readonly IStormApiClient _stormApi;
    private readonly IWorkItemMetricsService _metrics;
    private readonly ISprintAnalyticsService _analytics;

    public StormApiController(IStormApiClient stormApi, IWorkItemMetricsService metrics, ISprintAnalyticsService analytics)
    {
        _stormApi = stormApi;
        _metrics = metrics;
        _analytics = analytics;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true, platform = ".NET" });

    [HttpGet("workspaces")]
    public async Task<IActionResult> Workspaces(CancellationToken ct)
        => Ok(await _stormApi.PostAsync("admin/api/v1/workspaces/get", new { }, ct));

    [HttpGet("workspaces/{workspaceId}/analytics")]
    public async Task<IActionResult> ProjectAnalytics(string workspaceId, [FromQuery] string? workspaceName, CancellationToken ct)
        => Ok(await _analytics.GetProjectAnalyticsAsync(workspaceId, workspaceName, ct));

    [HttpGet("workspaces/{workspaceId}/folders/{folderId}/sprints/{sprintId}/analytics")]
    public async Task<IActionResult> SprintAnalytics(string workspaceId, string folderId, string sprintId, CancellationToken ct)
        => Ok(await _analytics.GetSprintAnalyticsAsync(workspaceId, folderId, sprintId, ct));

    [HttpGet("workspaces/{workspaceId}/folders")]
    public async Task<IActionResult> Folders(string workspaceId, [FromQuery] string? name, [FromQuery] string? parentId, [FromQuery] string? maxItemsCount, CancellationToken ct)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) q.Add($"name={Uri.EscapeDataString(name)}");
        if (!string.IsNullOrWhiteSpace(parentId)) q.Add($"parentId={Uri.EscapeDataString(parentId)}");
        if (!string.IsNullOrWhiteSpace(maxItemsCount)) q.Add($"maxItemsCount={Uri.EscapeDataString(maxItemsCount)}");

        var path = $"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/folders";
        if (q.Count > 0) path += "?" + string.Join('&', q);

        return Ok(await _stormApi.GetAsync(path, ct));
    }

    [HttpGet("workspaces/{workspaceId}/sprints")]
    public async Task<IActionResult> Sprints(string workspaceId, [FromQuery] string? folderId, [FromQuery] int? maxItemsCount, CancellationToken ct)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(folderId)) q.Add($"folderId={Uri.EscapeDataString(folderId)}");
        if (maxItemsCount.HasValue) q.Add($"maxItemsCount={Math.Min(maxItemsCount.Value, 100)}");
        var path = $"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/sprints";
        if (q.Count > 0) path += "?" + string.Join('&', q);

        return Ok(await _stormApi.GetAsync(path, ct));
    }

    [HttpGet("workspaces/{workspaceId}/folders/{folderId}/sprints")]
    public Task<IActionResult> FolderSprints(string workspaceId, string folderId, [FromQuery] int? maxItemsCount, CancellationToken ct)
        => Sprints(workspaceId, folderId, maxItemsCount, ct);

    [HttpGet("workspaces/{workspaceId}/folders/{folderId}/workitems")]
    public async Task<IActionResult> WorkItems(string workspaceId, string folderId, [FromQuery] int? maxItemsCount, [FromQuery] string? sprintId, [FromQuery] string? fromToken, CancellationToken ct)
    {
        var q = new List<string> { $"folderId={Uri.EscapeDataString(folderId)}" };
        q.Add($"maxItemsCount={maxItemsCount ?? 100}");
        if (!string.IsNullOrWhiteSpace(sprintId)) q.Add($"sprintId={Uri.EscapeDataString(sprintId)}");
        if (!string.IsNullOrWhiteSpace(fromToken)) q.Add($"fromToken={Uri.EscapeDataString(fromToken)}");

        var path = $"cwm/public/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/workitems?{string.Join('&', q)}";
        return Ok(await _stormApi.GetAsync(path, ct));
    }

    [HttpPost("workspaces/{workspaceId}/workitems/fact")]
    public async Task<IActionResult> Fact(string workspaceId, [FromBody] FactRequest request, CancellationToken ct)
    {
        var result = new Dictionary<string, object>();
        foreach (var workitemId in request.WorkitemIds.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var history = await LoadHistoryAsync(workspaceId, workitemId, ct);
            var metrics = new
            {
                fact = Math.Round(_metrics.CalculateInProgressMinutes(history), 1),
                factQA = Math.Round(_metrics.CalculateTestingMinutes(history), 1),
                handoffFact = Math.Round(_metrics.CalculateInAssessmentTestingMinutes(history), 1)
            };
            result[workitemId] = metrics;
        }

        return Ok(result);
    }

    [HttpGet("workspaces/{workspaceId}/workitems/{workitemId}/history/ready-to-test")]
    public async Task<IActionResult> ReadyToTest(string workspaceId, string workitemId, CancellationToken ct)
    {
        var history = await LoadHistoryAsync(workspaceId, workitemId, ct);
        return Ok(new { pairs = _metrics.ComputeReadyToTestPairs(history) });
    }

    [HttpPatch("workspaces/{workspaceId}/workitems/{workitemId}")]
    [HttpPut("workspaces/{workspaceId}/workitems/{workitemId}")]
    public async Task<IActionResult> UpdateWorkitem(string workspaceId, string workitemId, [FromBody] WorkItemUpdateRequest request, [FromQuery] string? folderId, CancellationToken ct)
    {
        var raw = request.OriginalEstimate ?? request.EstimatedTime;
        if (raw is null || raw < 0) return BadRequest(new { error = "originalEstimate must be a non-negative number (seconds)" });

        var updated = await _stormApi.SendWorkItemUpdateAsync(workspaceId, workitemId, (int)Math.Round(raw.Value), folderId, ct);
        return Ok(updated);
    }

    private async Task<List<HistoryEventDto>> LoadHistoryAsync(string workspaceId, string workitemId, CancellationToken ct)
    {
        var paths = new[]
        {
            $"history/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/workItems/{Uri.EscapeDataString(workitemId)}/history",
            $"history/api/v1/workspaces/{Uri.EscapeDataString(workspaceId)}/workitems/{Uri.EscapeDataString(workitemId)}/history"
        };

        foreach (var path in paths)
        {
            try
            {
                var json = await _stormApi.GetAsync(path, ct);
                var events = ExtractEvents(json);
                if (events.Count > 0) return events;
            }
            catch
            {
                // Пробуем альтернативный endpoint c другим регистром workItems/workitems.
            }
        }

        return [];
    }

    private static List<HistoryEventDto> ExtractEvents(JsonElement element)
    {
        JsonElement source = element;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("items", out var items)) source = items;
            else if (element.TryGetProperty("data", out var data)) source = data;
            else if (element.TryGetProperty("entries", out var entries)) source = entries;
        }

        if (source.ValueKind != JsonValueKind.Array) return [];

        var events = new List<HistoryEventDto>();
        foreach (var item in source.EnumerateArray())
        {
            var evt = JsonSerializer.Deserialize<HistoryEventDto>(item.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (evt is not null) events.Add(evt);
        }

        return events;
    }
}
