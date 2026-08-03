using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.FareSurcharges;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/fare-surcharges")]
public sealed class OperatorFareSurchargesController : ControllerBase
{
    private const string OperatorReadRoles = "OPERATOR_STAFF,OPERATOR_ADMIN";
    private const string OperatorWriteRoles = "OPERATOR_ADMIN";
    private readonly IMediator _mediator;

    public OperatorFareSurchargesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("settings")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<FareSurchargeSettingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FareSurchargeSettingDto>> GetSettings(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetFareSurchargeSettingQuery(GetRequiredOperatorId()), cancellationToken));

    [HttpPut("settings")]
    [Authorize(Roles = OperatorWriteRoles)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<FareSurchargeSettingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FareSurchargeSettingDto>> PutSettings(
        [FromBody] UpdateFareSurchargeSettingRequest request,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(
            new UpdateFareSurchargeSettingCommand(GetRequiredOperatorId(), request.IsEnabled),
            cancellationToken));

    [HttpGet("periods")]
    [Authorize(Roles = OperatorReadRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<FareSurchargePeriodDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<FareSurchargePeriodDto>>> ListPeriods(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(
            new ListFareSurchargePeriodsQuery(GetRequiredOperatorId(), page, pageSize),
            cancellationToken));

    [HttpPost("periods")]
    [Authorize(Roles = OperatorWriteRoles)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<FareSurchargePeriodDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FareSurchargePeriodDto>> CreatePeriod(
        [FromBody] CreateFareSurchargePeriodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new CreateFareSurchargePeriodCommand(
                GetRequiredOperatorId(),
                request.Name,
                request.StartDate,
                request.EndDate,
                request.SurchargePercent,
                request.IsActive ?? true),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPatch("periods/{periodId:guid}")]
    [Authorize(Roles = OperatorWriteRoles)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<FareSurchargePeriodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FareSurchargePeriodDto>> UpdatePeriod(
        Guid periodId,
        [FromBody] UpdateFareSurchargePeriodRequest request,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(
            new UpdateFareSurchargePeriodCommand(
                GetRequiredOperatorId(),
                periodId,
                request.Name,
                request.StartDate,
                request.EndDate,
                request.SurchargePercent,
                request.IsActive),
            cancellationToken));

    [HttpDelete("periods/{periodId:guid}")]
    [Authorize(Roles = OperatorWriteRoles)]
    [RequireIdempotencyKey]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeletePeriod(Guid periodId, CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new DeleteFareSurchargePeriodCommand(GetRequiredOperatorId(), periodId),
            cancellationToken);
        return NoContent();
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage fare surcharges.");
}
