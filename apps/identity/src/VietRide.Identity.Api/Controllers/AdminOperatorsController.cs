using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Admin.ApproveOperator;
using VietRide.Identity.Application.Features.Admin.CreateOperator;
using VietRide.Identity.Application.Features.Admin.GetOperatorDetail;
using VietRide.Identity.Application.Features.Admin.ListOperators;
using VietRide.Identity.Application.Features.Admin.ReactivateOperator;
using VietRide.Identity.Application.Features.Admin.RejectOperator;
using VietRide.Identity.Application.Features.Admin.SuspendOperator;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/admin/operators")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminOperatorsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminOperatorsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Lists operators for System Admin review and operations.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<OperatorListItemDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new ListOperatorsQuery(
                CurrentUserClaims.GetRole(User),
                page,
                pageSize,
                search,
                sortBy,
                sortDir,
                status),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Returns the complete operator profile for System Admin detail views.</summary>
    [HttpGet("{operatorId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminOperatorDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminOperatorDetailDto>> GetDetail(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new GetOperatorDetailQuery(CurrentUserClaims.GetRole(User), operatorId),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Creates an approved operator and initial OPERATOR_ADMIN account.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateOperatorResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateOperatorResponseDto>> Create(
        [FromBody] CreateOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var callerUserId = CurrentUserClaims.GetUserId(User);
        var callerRole = CurrentUserClaims.GetRole(User);

        var response = await _sender.Send(
            new CreateOperatorCommand(
                callerRole,
                callerUserId,
                request.Name,
                request.ContactEmail,
                request.ContactPhone,
                request.BusinessRegistrationNumber,
                request.TaxCode,
                request.AddressStreet,
                request.AddressWard,
                request.AddressDistrict,
                request.AddressProvince,
                request.RepresentativeName,
                request.RepresentativePhone,
                request.GetUnsupportedSubscriptionFields()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Approves a pending operator registration.</summary>
    [HttpPost("{operatorId:guid}/approve")]
    [ProducesResponseType(typeof(ApiResponse<ApproveOperatorResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApproveOperatorResponseDto>> Approve(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new ApproveOperatorCommand(CurrentUserClaims.GetRole(User), CurrentUserClaims.GetUserId(User), operatorId),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Rejects a pending operator registration.</summary>
    [HttpPost("{operatorId:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<RejectOperatorResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RejectOperatorResponseDto>> Reject(
        Guid operatorId,
        [FromBody] RejectOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new RejectOperatorCommand(CurrentUserClaims.GetRole(User), CurrentUserClaims.GetUserId(User), operatorId, request.Reason),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Suspends an approved operator.</summary>
    [HttpPost("{operatorId:guid}/suspend")]
    [ProducesResponseType(typeof(ApiResponse<SuspendOperatorResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SuspendOperatorResponseDto>> Suspend(
        Guid operatorId,
        [FromBody] SuspendOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new SuspendOperatorCommand(CurrentUserClaims.GetRole(User), CurrentUserClaims.GetUserId(User), operatorId, request.Reason),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Reactivates a suspended operator without changing its subscription.</summary>
    [HttpPost("{operatorId:guid}/reactivate")]
    [ProducesResponseType(typeof(ApiResponse<ReactivateOperatorResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReactivateOperatorResponseDto>> Reactivate(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new ReactivateOperatorCommand(
                CurrentUserClaims.GetRole(User),
                CurrentUserClaims.GetUserId(User),
                operatorId),
            cancellationToken);

        return Ok(response);
    }
}
