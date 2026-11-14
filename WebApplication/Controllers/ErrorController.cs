using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NeetBee.Web.Controllers;

[AllowAnonymous]
public sealed class ErrorController(ILogger<ErrorController> logger) : Controller
{
    [Route("error")]
    public IActionResult Index()
    {
        logger.LogWarning("Error page shown for request {Path}", HttpContext.Request.Path);
        return View();
    }
}
