using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.Stations;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/stations")]
public sealed class StationsController : ControllerBase
{
    private const string OperatorRoles = "OPERATOR_STAFF,OPERATOR_ADMIN";

    private readonly IMediator mediator;

    public StationsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("search")]
    [Authorize(Roles = OperatorRoles)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StationSearchResult>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<StationSearchResult>>> SearchAsync(
        [FromQuery(Name = "q")] string? q,
        [FromQuery] string? city,
        [FromQuery] string? province,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new SearchStationsQuery(q, city, province), cancellationToken));
    }
}
