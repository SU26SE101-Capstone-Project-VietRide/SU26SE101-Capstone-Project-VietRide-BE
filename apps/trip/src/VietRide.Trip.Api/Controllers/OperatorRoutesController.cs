using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.AlternativeRoutes;
using VietRide.Trip.Application.Features.Routes;
using VietRide.Trip.Application.Features.RouteStopFareTemplates;
using VietRide.Trip.Application.Features.RouteStops;

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
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RouteListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<RouteListItemDto>>> GetAsync(
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
                request.HasReturnRouteId,
                request.BaseFare,
                request.TotalDistanceKm,
                request.EstimatedDurationMinutes,
                request.IsActive),
            cancellationToken));
    }

    [HttpPut("{id:guid}/geometry")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<RouteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RouteDto>> PutGeometryAsync(
        Guid id,
        [FromBody] SetRouteGeometryRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new SetRouteGeometryCommand(GetRequiredOperatorId(), id, request.PathPolyline),
            cancellationToken));
    }

    [HttpPost("{id:guid}/stops")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<RouteStopDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RouteStopDto>> AddStopAsync(
        Guid id,
        [FromBody] AddRouteStopRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new AddRouteStopCommand(
                GetRequiredOperatorId(),
                id,
                request.StopId,
                request.OrderIndex,
                request.EstimatedDurationFromOriginMinutes,
                request.DistanceFromOriginKm,
                request.AllowPickup ?? true,
                request.AllowDropoff ?? true),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpDelete("{id:guid}/stops/{stopId:guid}")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyDictionary<string, bool>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyDictionary<string, bool>>> RemoveStopAsync(
        Guid id,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        await mediator.Send(new RemoveRouteStopCommand(GetRequiredOperatorId(), id, stopId), cancellationToken);
        return Ok(new Dictionary<string, bool> { ["deleted"] = true });
    }

    [HttpPost("{id:guid}/fare-templates")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<RouteStopFareTemplateDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RouteStopFareTemplateDto>> AddFareTemplateAsync(
        Guid id,
        [FromBody] CreateRouteStopFareTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new CreateRouteStopFareTemplateCommand(
                GetRequiredOperatorId(),
                id,
                request.StopId,
                request.FareFromThisStop,
                request.EffectiveFrom,
                request.EffectiveUntil),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet("{id:guid}/fare-templates")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RouteStopFareTemplateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<RouteStopFareTemplateDto>>> GetFareTemplatesAsync(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ListRouteStopFareTemplatesQuery(GetRequiredOperatorId(), id, page, pageSize),
            cancellationToken));
    }

    [HttpPost("{id:guid}/alternative-routes")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<AlternativeRouteDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AlternativeRouteDto>> AddAlternativeRouteAsync(
        Guid id,
        [FromBody] CreateAlternativeRouteRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new CreateAlternativeRouteCommand(
                GetRequiredOperatorId(),
                id,
                request.Name,
                request.Description,
                request.DestinationStationId,
                request.TotalDistanceKm,
                request.EstimatedDurationMinutes,
                (request.Stops ?? []).Select(ToAlternativeRouteStopInput).ToList()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet("{id:guid}/alternative-routes")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AlternativeRouteListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<AlternativeRouteListItemDto>>> GetAlternativeRoutesAsync(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ListAlternativeRoutesQuery(GetRequiredOperatorId(), id, page, pageSize),
            cancellationToken));
    }

    private static AlternativeRouteStopInput ToAlternativeRouteStopInput(AlternativeRouteStopRequest request)
        => new(
            request.StopId,
            request.OrderIndex,
            request.EstimatedDurationFromOriginMinutes,
            request.DistanceFromOriginKm);

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage routes.");
}
