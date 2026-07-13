using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Shuttle;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/shuttle-trips")]
public sealed class InternalShuttleTripsController : ControllerBase
{
    private readonly ISender _sender;

    public InternalShuttleTripsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{shuttleTripId:guid}/tracking-context")]
    [ProducesResponseType(typeof(ApiResponse<ShuttleTrackingContext>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ShuttleTrackingContext>> GetTrackingContext(
        Guid shuttleTripId,
        [FromQuery] Guid userId,
        [FromQuery] string role,
        [FromQuery] Guid? operatorId,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(
            new GetShuttleTrackingContextQuery(shuttleTripId, userId, role, operatorId),
            cancellationToken));
}
