using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NeetBee.Web.Controllers;

/// <summary>
/// The study pages linked from the dashboard sidebar. Each action renders a Razor view in Views/Study
/// inside the dashboard layout.
/// </summary>
[Authorize]
public sealed class StudyController(ILogger<StudyController> logger) : Controller
{
    public IActionResult Courses()
    {
        logger.LogInformation("GET Study/Courses requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult PreviousYearPapers()
    {
        logger.LogInformation("GET Study/PreviousYearPapers requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult McqPractice()
    {
        logger.LogInformation("GET Study/McqPractice requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult MockTests()
    {
        logger.LogInformation("GET Study/MockTests requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult AudioLearning()
    {
        logger.LogInformation("GET Study/AudioLearning requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult Pricing()
    {
        logger.LogInformation("GET Study/Pricing requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult Performance() => ComingSoon("Performance");

    public IActionResult SavedQuestions() => ComingSoon("Saved Questions");

    public IActionResult Achievements() => ComingSoon("Achievements");

    public IActionResult Settings() => ComingSoon("Settings");

    public IActionResult HelpSupport() => ComingSoon("Help & Support");

    /// <summary>Shows a placeholder page for a sidebar link whose content is not built yet.</summary>
    private ViewResult ComingSoon(string title)
    {
        logger.LogInformation("GET Study page '{Title}' requested by {UserName} (placeholder)", title, User.Identity?.Name);
        ViewData["Title"] = title;
        return View("ComingSoon");
    }
}
