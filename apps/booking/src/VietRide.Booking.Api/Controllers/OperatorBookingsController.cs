using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/operator/bookings")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorBookingsController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorBookingsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [AllowedQueryParameters("status", "tripId", "date", "passengerPhone", "bookingCode", "search", "page", "pageSize", "sortBy", "sortDir")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorBookingListItem>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<OperatorBookingListItem>>> List(
        [FromQuery] ListOperatorBookingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOperatorId(User, out var operatorId))
        {
            return Forbid();
        }

        var result = await _sender.Send(
            new ListOperatorBookingsQuery(
                operatorId,
                request.Status,
                request.TripId,
                request.Date,
                request.PassengerPhone,
                request.BookingCode,
                request.Page,
                request.PageSize,
                request.SortBy,
                request.SortDir,
                request.Search),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<OperatorBookingDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OperatorBookingDetailDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetOperatorId(User, out var operatorId))
        {
            return Forbid();
        }

        var result = await _sender.Send(
            new GetOperatorBookingDetailQuery(id, operatorId),
            cancellationToken);

        return Ok(result);
    }

    private static bool TryGetOperatorId(ClaimsPrincipal user, out Guid operatorId)
    {
        var value = user.FindFirstValue("operator_id")
            ?? user.FindFirstValue("operatorId");

        return Guid.TryParse(value, out operatorId) && operatorId != Guid.Empty;
    }
}
