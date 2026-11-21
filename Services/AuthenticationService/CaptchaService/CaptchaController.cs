using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthenticationService.CaptchaService;

[ApiController]
[Route("api/auth/captcha")]
[AllowAnonymous]
public sealed class CaptchaController(CaptchaGenerator generator, CaptchaValidator validator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CaptchaModel), StatusCodes.Status200OK)]
    public ActionResult<CaptchaModel> Create()
    {
        var (id, answer, imageData) = generator.Generate();
        validator.Store(id, answer);
        return Ok(new CaptchaModel(id, imageData));
    }
}
