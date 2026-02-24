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

    public StormApiController(IStormApiClient stormApi, IWorkItemMetricsService metrics)
    {
        _stormApi = stormApi;
        _metrics = metrics;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", platform = ".NET" });

    [HttpGet("workspaces")]
    public async Task<IActionResult> Workspaces(CancellationToken ct)
        => Ok(await _stormApi.GetWorkspacesAsync(ct));

    [HttpGet("workspaces/{workspaceId}/folders")]
    public async Task<IActionResult> Folders(string workspaceId, CancellationToken ct)
        => Ok(await _stormApi.GetFoldersAsync(workspaceId, ct));

    [HttpGet("workspaces/{workspaceId}/folders/{folderId}/workitems")]
    public async Task<IActionResult> WorkItems(string workspaceId, string folderId, CancellationToken ct)
        => Ok(await _stormApi.GetWorkItemsAsync(workspaceId, folderId, ct));

    [HttpPost("workspaces/{workspaceId}/workitems/fact")]
    public async Task<IActionResult> Fact(string workspaceId, [FromBody] FactRequest request, CancellationToken ct)
    {
        if (!DateTimeOffset.TryParse(request.PeriodStart, out var periodStart)
            || !DateTimeOffset.TryParse(request.PeriodEnd, out var periodEnd))
        {
            return BadRequest(new { error = "Invalid period format" });
        }

        var result = new Dictionary<string, double>();
        foreach (var workItemId in request.WorkItemIds)
        {
            var history = await _stormApi.GetWorkItemHistoryAsync(workspaceId, workItemId, ct);
            result[workItemId] = _metrics.CalculateFactMinutes(history, periodStart, periodEnd);
        }

        return Ok(result);
    }
}
