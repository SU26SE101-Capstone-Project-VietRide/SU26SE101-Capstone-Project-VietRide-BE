using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/vehicles")]
public sealed class OperatorVehiclesController : ControllerBase
{
    private const string OperatorReadRoles = "OPERATOR_STAFF,OPERATOR_ADMIN";
    private const string OperatorWriteRoles = "OPERATOR_ADMIN";

    private readonly IMediator mediator;

    public OperatorVehiclesController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpPost]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<VehicleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VehicleDto>> PostAsync(
        [FromBody] CreateVehicleRequest request,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new CreateVehicleCommand(
                GetRequiredOperatorId(),
                request.VehicleTypeId,
                request.LicensePlate,
                request.SeatLayoutJson,
                request.TotalSeats,
                request.MaxCargoWeightKg,
                request.MaxCargoVolumeM3,
                request.ImageUrls),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    [AllowedQueryParameters("page", "pageSize", "search", "searchIn", "sortBy", "sortDir", "vehicleTypeId", "status", "isActive")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<VehicleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<VehicleDto>>> GetAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? searchIn,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] Guid? vehicleTypeId,
        [FromQuery] string? status,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new ListVehiclesQuery(
                GetRequiredOperatorId(),
                page,
                pageSize,
                search,
                searchIn,
                sortBy,
                sortDir,
                vehicleTypeId,
                status,
                isActive),
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<VehicleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VehicleDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new GetVehicleQuery(GetRequiredOperatorId(), id),
            cancellationToken));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = OperatorWriteRoles)]
    [ProducesResponseType(typeof(ApiResponse<VehicleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VehicleDto>> PatchAsync(
        Guid id,
        [FromBody] UpdateVehicleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(
            new UpdateVehicleCommand(
                GetRequiredOperatorId(),
                id,
                request.VehicleTypeId,
                request.LicensePlate,
                request.SeatLayoutJson,
                request.HasSeatLayoutJson,
                request.TotalSeats,
                request.MaxCargoWeightKg,
                request.HasMaxCargoWeightKg,
                request.MaxCargoVolumeM3,
                request.HasMaxCargoVolumeM3,
                request.Status,
                request.IsActive,
                request.ImageUrls,
                request.HasImageUrls),
            cancellationToken));
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage vehicles.");
}
