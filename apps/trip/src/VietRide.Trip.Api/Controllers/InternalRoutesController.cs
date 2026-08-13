using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Filters;
using VietRide.Trip.Application.Features.Internal.Routes;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/routes")]
public sealed class InternalRoutesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalRoutesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{routeId:guid}/ownership")]
    [ProducesResponseType(typeof(RouteOwnershipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteOwnershipDto>> GetOwnershipAsync(
        Guid routeId,
        [FromQuery] Guid operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetRouteOwnershipQuery(routeId, operatorId),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("search")]
    [AllowedQueryParameters("operatorId", "search")]
    [ProducesResponseType(typeof(InternalRouteSearchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<InternalRouteSearchDto>> SearchAsync(
        [FromQuery] Guid operatorId,
        [FromQuery] string search,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SearchInternalRoutesQuery(operatorId, search),
            cancellationToken);
        return Ok(result);
    }
}
