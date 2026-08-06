using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Application.Features.Stations.MergeStations;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/admin/stations")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminStationsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<StationDto>>> GetAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search,
        [FromQuery] bool? isActive, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListAdminStationsQuery(page, pageSize, search, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StationDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAdminStationQuery(id), cancellationToken));

    [HttpPatch("{id:guid}")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StationDto>> PatchAsync(Guid id, [FromBody] UpdateAdminStationRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateAdminStationCommand(id, request.Name, request.AddressStreet,
            request.LocationId, request.City, request.Ward, request.Latitude, request.Longitude,
            request.ContactPhone, request.ContactEmail, request.OperatingHours?.GetRawText(),
            request.Facilities?.GetRawText(), request.SupportsShuttle, request.IsActive,
            CurrentUserClaims.GetUserId(User), HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()), cancellationToken));

    [HttpPost("{primaryStationId:guid}/merge")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<MergeStationsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MergeStationsResponse>> MergeAsync(
        Guid primaryStationId,
        [FromBody] MergeStationsRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new MergeStationsCommand(
            primaryStationId,
            request.DuplicateId,
            CurrentUserClaims.GetUserId(User),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()), cancellationToken));

    [HttpDelete("{id:guid}")]
    [RequireIdempotencyKey]
    public async Task<ActionResult<StationDto>> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteAdminStationCommand(id), cancellationToken));
}
