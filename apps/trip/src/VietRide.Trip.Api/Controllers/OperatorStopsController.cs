using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/stops")]
public sealed class OperatorStopsController : ControllerBase
{
    private const string OperatorReadRoles = "OPERATOR_STAFF,OPERATOR_ADMIN";
    private const string OperatorWriteRoles = "OPERATOR_ADMIN";

    private readonly IMediator mediator;

    public OperatorStopsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpPost]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<StopDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StopDto>> PostAsync(
        [FromBody] CreateStopRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = GetRequiredOperatorId();
        var response = await mediator.Send(
            new CreateStopCommand(
                operatorId,
                request.Name,
                request.Latitude,
                request.Longitude,
                request.Description,
                request.Address,
                request.GooglePlaceId,
                request.LocationId,
                request.LocationCode),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<StopDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<StopDto>>> GetAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ListStopsQuery(GetRequiredOperatorId(), page, pageSize, search),
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<StopDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StopDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetStopQuery(GetRequiredOperatorId(), id), cancellationToken));
    }

    [HttpPatch("{id:guid}")]
    [RequireIdempotencyKey]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<StopDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StopDto>> PatchAsync(
        Guid id,
        [FromBody] UpdateStopRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new UpdateStopCommand(
                GetRequiredOperatorId(),
                id,
                request.Name,
                request.Latitude,
                request.Longitude,
                request.Description,
                request.Address,
                request.GooglePlaceId),
            cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [RequireIdempotencyKey]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<DisableStopResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<DisableStopResponse>> DeleteAsync(
        Guid id, [FromQuery] Guid? replacedByStopId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DisableStopCommand(GetRequiredOperatorId(), id, replacedByStopId), cancellationToken));

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage stops.");
}
