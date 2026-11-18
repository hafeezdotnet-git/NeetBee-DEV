using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeetBee.Web.Models;
using NeetBee.Web.Services;

namespace NeetBee.Web.Controllers;

[Authorize]
public sealed class DashboardController(IStudentProgressService progress, ILogger<DashboardController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name ?? "Student";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        logger.LogInformation("GET Dashboard/Index requested by {UserName}", userName);

        var studentProgress = await progress.GetAsync(userId, cancellationToken);
        return View(new DashboardViewModel { UserName = userName, Progress = studentProgress });
    }
}
