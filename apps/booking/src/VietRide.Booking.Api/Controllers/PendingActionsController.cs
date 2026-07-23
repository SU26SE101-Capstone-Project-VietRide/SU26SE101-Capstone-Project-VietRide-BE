using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Booking.Application.Features.PendingActions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/bookings/{bookingId}/pending-actions")]
public sealed class PendingActionsController(ISender sender) : ControllerBase
{
    [HttpPost("{actionId}/resolve")]
    [Authorize(Roles = "PASSENGER")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<ResolvePendingActionResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Resolve(
        [FromRoute] string bookingId,
        [FromRoute] string actionId,
        [FromBody] ResolvePendingActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ResolveRouteChangePendingActionCommand(
            ParseRouteGuid(bookingId, nameof(bookingId)),
            ParseRouteGuid(actionId, nameof(actionId)),
            GetPassengerUserId(),
            Request.Headers["Idempotency-Key"].ToString(),
            request.Action,
            request.SelectedStopId,
            request.SelectedStationId,
            request.Note,
            request.ExtraFields?.Keys.ToArray() ?? []), cancellationToken);
        return StatusCode(StatusCodes.Status200OK, result);
    }

    private Guid GetPassengerUserId()
    {
        var value = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out var passengerId) && passengerId != Guid.Empty)
        {
            return passengerId;
        }

        throw new UnauthorizedAccessException(
            "Authenticated caller sub claim is missing or invalid.");
    }

    private static Guid ParseRouteGuid(string value, string field)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        throw new CodedValidationException(
            "VALIDATION_ERROR",
            "Route value must be a UUID.",
            [new ValidationError(field, "Must be a valid UUID.")]);
    }
}
