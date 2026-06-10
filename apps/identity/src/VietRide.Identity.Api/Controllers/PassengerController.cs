using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Application.Features.Passenger.GetPassengerBookings;
using VietRide.Identity.Application.Features.Users.GetMe;
using VietRide.Shared.Kernel.Primitives;
using PagedResult = VietRide.Shared.Application.Pagination.PagedResult<object>;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// Passenger-facing profile and booking-history endpoints for the authenticated caller.
/// STUB endpoints (Day 10): the profile reuses the GetMe projection verbatim and the
/// booking history returns an empty paginated envelope — item schema finalized in
/// Sprint 3 (SCV-76 / Booking). Responses are wrapped by ADR 0004 ApiResponse filters.
/// </summary>
[ApiController]
[Authorize]
[Route("v1/passenger")]
public sealed class PassengerController : ControllerBase
{
    private readonly ISender _sender;

    public PassengerController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Return the authenticated passenger profile (reuses the GetMe projection).</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<GetMeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var result = await _sender.Send(new GetMeQuery(CurrentUserClaims.GetUserId(User)), ct);
        return Ok(result);
    }

    /// <summary>
    /// Return the authenticated passenger booking history.
    /// STUB — item schema finalized in Sprint 3 (SCV-76 / Booking); currently always empty.
    /// </summary>
    [HttpGet("bookings")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBookings(CancellationToken ct)
    {
        var result = await _sender.Send(
            new GetPassengerBookingsQuery(CurrentUserClaims.GetUserId(User)),
            ct);
        return Ok(result);
    }
}
