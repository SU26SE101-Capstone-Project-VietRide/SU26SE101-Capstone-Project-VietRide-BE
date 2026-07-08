using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Promotions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/promotions")]
[AllowAnonymous]
public sealed class PromotionsController : ControllerBase
{
    private readonly ISender _sender;

    public PromotionsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PromotionItem>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPromotions([FromQuery] string service, CancellationToken ct)
    {
        var result = await _sender.Send(new GetPromotionsQuery(service), ct);
        return Ok(result);
    }
}
