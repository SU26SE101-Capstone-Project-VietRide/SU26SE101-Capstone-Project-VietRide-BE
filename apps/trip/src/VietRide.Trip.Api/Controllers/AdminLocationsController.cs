using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.Locations;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/admin/locations")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminLocationsController : ControllerBase
{
    private readonly IMediator mediator;

    public AdminLocationsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<LocationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<LocationDto>>> GetAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ListAdminLocationsQuery(page, pageSize, search, isActive),
            cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LocationDto>> PostAsync(
        [FromBody] CreateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new CreateLocationCommand(
                request.Code,
                request.Name,
                request.Type,
                request.SortOrder,
                request.IsActive ?? true,
                request.ParentCode),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LocationDto>> PatchAsync(
        Guid id,
        [FromBody] UpdateLocationRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new UpdateLocationCommand(
                id,
                request.Code,
                request.Name,
                request.Type,
                request.SortOrder,
                request.IsActive,
                request.ParentCode),
            cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LocationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LocationDto>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new DeactivateLocationCommand(id), cancellationToken));
    }
}
