using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Bookings.History;
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

    [HttpGet("history")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BookingHistoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<BookingHistoryItemDto>>> GetHistoryAsync(
        [FromQuery] Guid userId,
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetBookingHistoryQuery(userId, status, from, to, page, pageSize),
            cancellationToken);

        return Ok(result);
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

    [HttpGet("trips/{tripId:guid}/edit-impact")]
    [ProducesResponseType(typeof(TripEditImpactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TripEditImpactDto>> GetTripEditImpactAsync(
        Guid tripId,
        [FromQuery] Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetTripEditImpactQuery(tripId, operatorId ?? Guid.Empty),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("trips/{tripId}/notification-recipients")]
    [ProducesResponseType(typeof(TripNotificationRecipientsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TripNotificationRecipientsDto>> GetTripNotificationRecipientsAsync(
        string tripId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetTripNotificationRecipientsQuery(tripId),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("trips/{tripId}/vehicle-substitution-impact")]
    [ProducesResponseType(typeof(VehicleSubstitutionImpactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VehicleSubstitutionImpactDto>> GetVehicleSubstitutionImpactAsync(
        string tripId,
        [FromQuery] string? operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetVehicleSubstitutionImpactQuery(tripId, operatorId),
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

    [HttpGet("trips/{tripId}/stops/{stopId}/pending-passenger-count")]
    [ProducesResponseType(typeof(PendingPassengerCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PendingPassengerCountDto>> GetPendingPassengerCountAsync(
        string tripId,
        string stopId,
        [FromQuery] string? operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetPendingPassengerCountQuery(tripId, stopId, operatorId),
            cancellationToken);

        return Ok(result);
    }
}
