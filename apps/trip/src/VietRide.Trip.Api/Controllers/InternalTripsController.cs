using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Middleware;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.Internal.Trips.LockRoundTripSeats;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/trips")]
public sealed class InternalTripsController : ControllerBase
{
    private readonly IMediator mediator;

    public InternalTripsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpPost("round-trip/lock-seats")]
    [ProducesResponseType(typeof(ApiResponse<LockRoundTripSeatsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<LockRoundTripSeatsResult>>> LockRoundTripSeatsAsync(
        [FromBody] LockRoundTripSeatsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Idempotency-Key header is required.",
                [new ValidationError("Idempotency-Key", "Idempotency-Key header is required.")]);
        }

        var result = await mediator.Send(
            new LockRoundTripSeatsCommand(
                new Application.Features.Internal.Trips.LockRoundTripSeats.LockRoundTripSeatsLegRequest(
                    request.Outbound.TripId,
                    request.Outbound.SeatNumbers),
                new Application.Features.Internal.Trips.LockRoundTripSeats.LockRoundTripSeatsLegRequest(
                    request.Return.TripId,
                    request.Return.SeatNumbers),
                request.HoldOwnerId,
                request.TtlSeconds,
                idempotencyKey.Trim()),
            cancellationToken);

        var traceId = HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var id)
            ? id?.ToString() ?? string.Empty
            : HttpContext.TraceIdentifier;

        return Ok(ApiResponse<LockRoundTripSeatsResult>.Ok(result, ApiMeta.Create(traceId)));
    }
}
