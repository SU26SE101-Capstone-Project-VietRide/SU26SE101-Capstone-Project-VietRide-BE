using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Vouchers.AvailableVouchers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/vouchers")]
[Authorize]
public sealed class VouchersController : ControllerBase
{
    private readonly ISender _sender;

    public VouchersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("available")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AvailableVoucherItem>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailable(
        [FromQuery] string service,
        [FromQuery] Guid? tripId,
        [FromQuery] Guid? operatorId,
        [FromQuery] Guid? routeId,
        [FromQuery] string? paymentMethod,
        [FromQuery] long? orderAmount,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new GetAvailableVouchersQuery(
                GetUserId(),
                service,
                tripId,
                operatorId,
                routeId,
                paymentMethod,
                orderAmount),
            ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");
        return userId;
    }
}
