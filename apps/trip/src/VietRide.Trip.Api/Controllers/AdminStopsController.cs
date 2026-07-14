using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/admin/stops")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminStopsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<StopDto>>> GetAsync(
        [FromQuery] Guid? operatorId, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListAdminStopsQuery(operatorId, page, pageSize, search, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StopDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAdminStopQuery(id), cancellationToken));

    [HttpPatch("{id:guid}")]
    [RequireIdempotencyKey]
    public async Task<ActionResult<StopDto>> PatchAsync(Guid id, [FromBody] UpdateAdminStopRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateAdminStopCommand(id, request.Name, request.Latitude,
            request.Longitude, request.Description, request.Address, request.GooglePlaceId, request.IsActive), cancellationToken));

    [HttpDelete("{id:guid}")]
    [RequireIdempotencyKey]
    public async Task<ActionResult<DisableStopResponse>> DeleteAsync(
        Guid id, [FromQuery] Guid? replacedByStopId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DisableStopCommand(null, id, replacedByStopId), cancellationToken));
}
