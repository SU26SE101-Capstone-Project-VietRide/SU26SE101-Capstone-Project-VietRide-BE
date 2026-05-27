using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("v1/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        service = "Trip",
        status = "ok",
        timestamp = DateTime.UtcNow
    });
}
