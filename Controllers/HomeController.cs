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
}
