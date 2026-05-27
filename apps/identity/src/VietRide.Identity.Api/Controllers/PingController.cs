using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VietRide.Identity.Api.Controllers;

/// Anonymous liveness ping. Distinct from `/health` (which checks downstream deps).
[ApiController]
[AllowAnonymous]
[Route("v1/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        service = "Identity",
        status = "ok",
        timestamp = DateTime.UtcNow
    });
}
