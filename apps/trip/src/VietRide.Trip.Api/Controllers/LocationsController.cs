using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.Locations;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/locations")]
public sealed class LocationsController : ControllerBase
{
    private readonly IMediator mediator;

    public LocationsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LocationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> GetAsync(
        [FromQuery] string? parentCode,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new ListLocationsQuery(parentCode, search), cancellationToken));
    }
}
