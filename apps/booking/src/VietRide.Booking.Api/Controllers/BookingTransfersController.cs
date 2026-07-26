using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.BookingTransfers.ConfirmPassengerTransfer;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Booking.Api.Controllers;

/// <summary>Driver and assistant confirmation endpoints for replacement-trip transfers.</summary>
[ApiController]
[Route("v1/bookings/trips/{newTripId}/transfers")]
[Authorize(Roles = "DRIVER,ASSISTANT")]
public sealed class BookingTransfersController(ISender sender) : ControllerBase
{
    /// <summary>Confirms one passenger's physical transfer to the replacement trip.</summary>
    [HttpPost("passengers/{passengerId}/confirm")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<ConfirmPassengerTransferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConfirmPassengerTransferResponse>> ConfirmPassengerTransfer(
        [FromRoute] Guid newTripId,
        [FromRoute] Guid passengerId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ConfirmPassengerTransferCommand(newTripId, passengerId, GetCallerUserId()),
            cancellationToken);

        return Ok(result);
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
