using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
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

    [HttpGet]
    [Authorize(Roles = "OPERATOR_STAFF,OPERATOR_ADMIN")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DriverScheduleDetailDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<DriverScheduleDetailDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] Guid? routeId,
        [FromQuery] Guid? driverUserId, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage driver schedules.");
        return Ok(await sender.Send(new ListDriverSchedulesQuery(operatorId, page, pageSize, routeId, driverUserId, isActive), cancellationToken));
    }

    [HttpPost]
    [SkipIdempotency("DriverSchedule creation retains its legacy no-key contract; business conflict guards prevent duplicate active schedules.")]
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

    [HttpPatch("{id:guid}/activate")]
    [SkipIdempotency("DriverSchedule activation is behavior-idempotent and explicitly requires no Idempotency-Key.")]
    [ProducesResponseType(typeof(ApiResponse<DriverScheduleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DriverScheduleDto>> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage driver schedules.");

        var result = await sender.Send(
            new ActivateDriverScheduleCommand(operatorId, id),
            cancellationToken);

        return Ok(result);
    }

    [HttpPatch("{id:guid}/crew")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<DriverScheduleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DriverScheduleDto>> UpdateCrew(
        Guid id,
        [FromBody] UpdateDriverScheduleCrewRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage driver schedules.");

        return Ok(await sender.Send(
            new UpdateDriverScheduleCrewCommand(
                operatorId,
                id,
                CurrentUserClaims.GetUserId(User),
                GetRequestId(),
                request.DriverUserId,
                request.AssistantUserId),
            cancellationToken));
    }

    [HttpPatch("{id:guid}")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<DriverScheduleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DriverScheduleDto>> Update(
        Guid id,
        [FromQuery] string? applyTo,
        [FromBody] UpdateDriverScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage driver schedules.");

        return Ok(await sender.Send(
            new UpdateDriverScheduleCommand(
                operatorId,
                id,
                CurrentUserClaims.GetUserId(User),
                GetRequestId(),
                applyTo?.Trim().ToUpperInvariant() ?? string.Empty,
                request.DepartureTimeSpecified,
                request.DepartureTime,
                request.DayOfWeekSpecified,
                request.DayOfWeek,
                request.DriverUserIdSpecified,
                request.DriverUserId,
                request.AssistantUserIdSpecified,
                request.AssistantUserId,
                request.VehicleIdSpecified,
                request.VehicleId,
                request.ValidUntilSpecified,
                request.ValidUntil,
                request.IsActiveSpecified,
                request.IsActive),
            cancellationToken));
    }

    private string GetRequestId() =>
        HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var value)
        && value is string requestId
        && !string.IsNullOrWhiteSpace(requestId)
            ? requestId
            : HttpContext.TraceIdentifier;
}
