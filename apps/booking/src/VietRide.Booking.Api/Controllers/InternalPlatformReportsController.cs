using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("internal/v1/reports/platform")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalPlatformReportsController : ControllerBase
{
    private readonly ISender _sender;

    public InternalPlatformReportsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("bookings")]
    [ProducesResponseType(typeof(PlatformBookingReportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlatformBookingReportResult>> GetBookingsAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetPlatformBookingReportQuery(from, to),
            cancellationToken);
        return Ok(result);
    }
}
