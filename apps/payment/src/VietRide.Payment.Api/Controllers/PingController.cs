using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("v1/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        service = "Payment",
        status = "ok",
        timestamp = DateTime.UtcNow
    });
}
