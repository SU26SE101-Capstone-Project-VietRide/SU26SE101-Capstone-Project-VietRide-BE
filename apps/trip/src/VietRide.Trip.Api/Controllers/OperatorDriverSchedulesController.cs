using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.DriverSchedules;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/driver-schedules")]
[Authorize(Roles = "OPERATOR_ADMIN")]
public sealed class OperatorDriverSchedulesController : ControllerBase
{
    private readonly ISender sender;

    public OperatorDriverSchedulesController(ISender sender)
    {
        this.sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<DriverScheduleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DriverScheduleDto>> Create(
        [FromBody] CreateDriverScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage driver schedules.");

        var result = await sender.Send(
            new CreateDriverScheduleCommand(
                operatorId,
                request.RouteId,
                request.VehicleId,
                request.DriverUserId,
                request.AssistantUserId,
                request.DayOfWeek,
                request.DepartureTime,
                request.ValidFrom,
                request.ValidUntil,
                request.IsActive),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }
}
