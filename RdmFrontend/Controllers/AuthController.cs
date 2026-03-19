using Microsoft.AspNetCore.Mvc;

namespace RdmApi.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    [HttpGet("callback")]
    public IActionResult Callback()
    {
        // OAuth / OIDC provider will redirect the user here
        // Later we will process the token

        return Ok("Authentication callback received");
    }
}
