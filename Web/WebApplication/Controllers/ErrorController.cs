using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NeetBee.Web.Controllers;

/// <summary>
/// "System Error" page. Shown for unhandled exceptions (UseExceptionHandler) and for error status codes
/// such as 404 (UseStatusCodePagesWithReExecute), see Program.cs.
/// </summary>
[AllowAnonymous]
public sealed class ErrorController(ILogger<ErrorController> logger) : Controller
{
    [Route("error")]
    public IActionResult Index()
    {
        var statusFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        var path = statusFeature?.OriginalPath
            ?? HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path
            ?? HttpContext.Request.Path.Value;

        logger.LogWarning("Error page shown for request {Path} with status {StatusCode}", path, Response.StatusCode);

        // Script calls (fetch) only need the status code, not an HTML page.
        var acceptsHtml = Request.Headers.Accept.Any(a => a?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
        if (!acceptsHtml && statusFeature is not null)
            return new EmptyResult();

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        return View();
    }
}
