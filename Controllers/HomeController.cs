using Microsoft.AspNetCore.Mvc;

namespace TeamStorm.Metrics.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
