using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("internal/v1/bookings")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalBookingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalBookingsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<InternalBookingSnapshotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InternalBookingSnapshotDto>> GetBookingSnapshotAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetInternalBookingSnapshotQuery(bookingId),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("payment-context/{referenceType}/{referenceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentContextSnapshotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentContextSnapshotDto>> GetPaymentContextSnapshotAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetPaymentContextSnapshotQuery(referenceType, referenceId),
            cancellationToken);

        return Ok(result);
    }
}
