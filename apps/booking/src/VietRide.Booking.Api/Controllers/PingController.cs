using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("v1/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        service = "Booking",
        status = "ok",
        timestamp = DateTime.UtcNow
    });
}
