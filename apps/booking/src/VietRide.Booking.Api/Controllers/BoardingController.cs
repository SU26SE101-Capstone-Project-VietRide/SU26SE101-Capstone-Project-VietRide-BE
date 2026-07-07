using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;
using VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;
using VietRide.Booking.Application.Features.Manifest.GetTripManifest;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

/// <summary>Driver and assistant operational endpoints for a trip.</summary>
[ApiController]
[Route("v1/bookings/trips/{tripId:guid}")]
[Authorize(Roles = "DRIVER,ASSISTANT")]
public sealed class BoardingController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISender _sender;

    public BoardingController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Returns the PII-free passenger manifest ordered by pickup point.</summary>
    [HttpGet("manifest")]
    [ProducesResponseType(typeof(ApiResponse<GetTripManifestResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GetTripManifestResult>> GetTripManifest(
        [FromRoute] Guid tripId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetTripManifestQuery(tripId, GetCallerUserId()),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Marks one passenger record as boarded for the assigned trip.</summary>
    [HttpPost("boarding/passenger/{passengerRecordId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TickPassengerBoardedResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TickPassengerBoardedResult>> TickPassengerBoarded(
        [FromRoute] Guid tripId,
        [FromRoute] Guid passengerRecordId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKeyIsPresent();

        var result = await _sender.Send(
            new TickPassengerBoardedCommand(tripId, passengerRecordId, GetCallerUserId()),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Resolves a ticket QR code to its passenger boarding record.</summary>
    [HttpPost("boarding/qr-scan")]
    [ProducesResponseType(typeof(ApiResponse<ScanBookingCodeForTripResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScanBookingCodeForTripResult>> ScanBookingCodeForTrip(
        [FromRoute] Guid tripId,
        [FromBody] ScanBookingCodeForTripRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ScanBookingCodeForTripQuery(
                tripId,
                request.TicketCode,
                request.BookingCode,
                GetCallerUserId()),
            cancellationToken);

        return Ok(result);
    }

    private void EnsureIdempotencyKeyIsPresent()
    {
        var value = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            ? values.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new VietRide.Shared.Application.Exceptions.CodedValidationException(
                "VALIDATION_ERROR",
                "Idempotency-Key header is required.");
        }
    }

    private Guid GetCallerUserId()
    {
        var sub = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
        {
            throw new UnauthorizedAccessException(
                "Authenticated caller sub claim is missing or invalid.");
        }

        return userId;
    }
}
