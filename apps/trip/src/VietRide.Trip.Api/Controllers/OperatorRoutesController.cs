using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.Routes;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/routes")]
public sealed class OperatorRoutesController : ControllerBase
{
    private const string OperatorReadRoles = "OPERATOR_STAFF,OPERATOR_ADMIN";
    private const string OperatorWriteRoles = "OPERATOR_ADMIN";

    private readonly IMediator mediator;

    public OperatorRoutesController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpPost]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<RouteDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RouteDto>> PostAsync(
        [FromBody] CreateRouteRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = GetRequiredOperatorId();
        var response = await mediator.Send(
            new CreateRouteCommand(
                operatorId,
                request.Name,
                request.OriginStationId,
                request.DestinationStationId,
                request.ReturnRouteId,
                request.BaseFare,
                request.TotalDistanceKm,
                request.EstimatedDurationMinutes,
                request.IsActive),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RouteDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<RouteDto>>> GetAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ListRoutesQuery(GetRequiredOperatorId(), page, pageSize, search),
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<RouteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetRouteQuery(GetRequiredOperatorId(), id), cancellationToken));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<RouteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RouteDto>> PatchAsync(
        Guid id,
        [FromBody] UpdateRouteRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new UpdateRouteCommand(
                GetRequiredOperatorId(),
                id,
                request.Name,
                request.ReturnRouteId,
                request.BaseFare,
                request.TotalDistanceKm,
                request.EstimatedDurationMinutes,
                request.IsActive),
            cancellationToken));
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage routes.");
}
