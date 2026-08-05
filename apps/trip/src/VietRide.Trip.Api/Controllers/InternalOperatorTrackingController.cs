using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Authentication;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/operators/{operatorId:guid}/tracking-trips")]
public sealed class InternalOperatorTrackingController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OperatorTrackingTripDto>>> GetAsync(
        Guid operatorId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListOperatorTrackingTripsQuery(operatorId, status), cancellationToken));
}
