using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NeetBee.Web.Controllers;

/// <summary>
/// "System Under Maintenance" page. Switched on by App:SystemInMaintenance = "Y" in appsettings.json
/// (or the App__SystemInMaintenance app setting / environment variable). While it is on, the middleware
/// in Program.cs sends every request here.
/// </summary>
[AllowAnonymous]
public sealed class MaintenanceController : Controller
{
    public const string PagePath = "/maintenance";
    public const string ImagePath = "/img/maintenance.webp";

    // Portrait image for phones. Put it in wwwroot/img under one of these names; the page uses the first
    // one that exists and falls back to the desktop image when none does.
    public static readonly string[] MobileImagePaths =
        { "/img/maintenance-mobile.webp", "/img/maintenance-mobile.png", "/img/maintenance-mobile.jpg" };

    public static bool IsMaintenanceImage(PathString path) =>
        path.Equals(ImagePath, StringComparison.OrdinalIgnoreCase)
        || MobileImagePaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase));

    public static bool IsEnabled(IConfiguration configuration)
    {
        var value = configuration["App:SystemInMaintenance"]?.Trim();
        return string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    [Route("maintenance")]
    public IActionResult Index([FromServices] IConfiguration configuration, [FromServices] IWebHostEnvironment environment)
    {
        // Once maintenance is switched off, an old bookmark or refresh goes back to the site.
        if (!IsEnabled(configuration))
            return Redirect("/");

        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        Response.Headers.RetryAfter = "600";
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        ViewData["MobileImage"] = MobileImagePaths
            .FirstOrDefault(p => environment.WebRootFileProvider.GetFileInfo(p).Exists);
        return View();
    }
}
