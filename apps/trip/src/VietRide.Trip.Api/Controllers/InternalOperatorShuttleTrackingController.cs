using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Authentication;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/operators/{operatorId:guid}/tracking-shuttle-trips")]
public sealed class InternalOperatorShuttleTrackingController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalOperatorShuttleTrackingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OperatorTrackingShuttleTripDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<OperatorTrackingShuttleTripDto>>> GetAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(
            new ListOperatorTrackingShuttleTripsQuery(operatorId),
            cancellationToken));
}
