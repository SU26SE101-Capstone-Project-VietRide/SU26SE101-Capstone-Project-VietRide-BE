using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Admin.ApproveOperator;
using VietRide.Identity.Application.Features.Admin.CreateOperator;
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
}
