using Microsoft.AspNetCore.Mvc;

namespace TeamStorm.Metrics.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    [HttpGet("projects/{workspaceId}")]
    public IActionResult Project(string workspaceId, [FromQuery] string? name = null, [FromQuery] string? key = null)
    {
        ViewData["WorkspaceId"] = workspaceId;
        ViewData["WorkspaceName"] = name ?? "Project";
        ViewData["WorkspaceKey"] = key ?? string.Empty;
        return View();
    }

    [HttpGet("projects/{workspaceId}/folders/{folderId}/sprints/{sprintId}")]
    public IActionResult Sprint(string workspaceId, string folderId, string sprintId, [FromQuery] string? workspaceName = null)
    {
        ViewData["WorkspaceId"] = workspaceId;
        ViewData["FolderId"] = folderId;
        ViewData["SprintId"] = sprintId;
        ViewData["WorkspaceName"] = workspaceName ?? workspaceId;
        return View();
    }
}
