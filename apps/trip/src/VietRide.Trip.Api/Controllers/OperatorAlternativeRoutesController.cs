using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.AlternativeRoutes;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/alternative-routes")]
public sealed class OperatorAlternativeRoutesController : ControllerBase
{
    private const string OperatorWriteRoles = "OPERATOR_ADMIN";

    private readonly IMediator mediator;

    public OperatorAlternativeRoutesController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<AlternativeRouteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AlternativeRouteDto>> PatchAsync(
        Guid id,
        [FromBody] UpdateAlternativeRouteRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new UpdateAlternativeRouteCommand(
                GetRequiredOperatorId(),
                id,
                request.Name,
                request.HasName,
                request.Description,
                request.HasDescription,
                request.DestinationStationId,
                request.HasDestinationStationId,
                request.TotalDistanceKm,
                request.HasTotalDistanceKm,
                request.EstimatedDurationMinutes,
                request.HasEstimatedDurationMinutes,
                request.IsActive,
                request.HasStops,
                request.Stops?.Select(ToInput).ToList()),
            cancellationToken));
    }

    [HttpPut("{id:guid}/geometry")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<AlternativeRouteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AlternativeRouteDto>> PutGeometryAsync(
        Guid id,
        [FromBody] SetRouteGeometryRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new SetAlternativeRouteGeometryCommand(GetRequiredOperatorId(), id, request.PathPolyline),
            cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyDictionary<string, bool>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyDictionary<string, bool>>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await mediator.Send(new DeactivateAlternativeRouteCommand(GetRequiredOperatorId(), id), cancellationToken);
        return Ok(new Dictionary<string, bool> { ["isActive"] = false });
    }

    private static AlternativeRouteStopInput ToInput(AlternativeRouteStopRequest request)
        => new(
            request.StopId,
            request.OrderIndex,
            request.EstimatedDurationFromOriginMinutes,
            request.DistanceFromOriginKm);

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage alternative routes.");
}
