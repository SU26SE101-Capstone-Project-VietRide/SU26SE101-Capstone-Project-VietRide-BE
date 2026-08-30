using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Application.Features.Reliability.Forwarding;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Application.Features.Reliability.Policies;
using VietRide.Parcel.Application.Features.Reliability.Reconciliation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/operator/parcel-incidents")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorParcelIncidentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OperatorParcelIncidentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("~/v1/operator/parcel-stop-departure-approvals/{requestId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ParcelStopDepartureApprovalResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelStopDepartureApprovalResponse>> GetStopDepartureApprovalAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetParcelStopDepartureApprovalQuery(
                requestId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                CurrentUserClaims.GetRole(User)),
            cancellationToken));
    }

    [HttpPost("~/v1/operator/parcel-stop-departure-approvals/{requestId:guid}/decision")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelStopDepartureApprovalResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ParcelStopDepartureApprovalResponse>> DecideStopDepartureApprovalAsync(
        Guid requestId,
        [FromBody] DecideParcelStopDepartureApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new DecideParcelStopDepartureApprovalCommand(
                requestId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                CurrentUserClaims.GetRole(User),
                request.Decision?.Trim().ToUpperInvariant() ?? string.Empty,
                request.Note,
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString())),
            cancellationToken));
    }

    [HttpPost("{incidentId:guid}/custody-exception-decision")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReportCustodyExceptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReportCustodyExceptionResponse>> DecideCustodyExceptionAsync(
        Guid incidentId,
        [FromBody] DecideCustodyExceptionRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var result = await _mediator.Send(
            new DecideCustodyExceptionCommand(
                incidentId,
                "INCIDENT",
                CurrentUserClaims.GetUserId(User),
                operatorId,
                CurrentUserClaims.GetRole(User),
                request.Decision?.Trim().ToUpperInvariant() ?? string.Empty,
                request.Note,
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString())),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ParcelIncidentListItem>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ParcelIncidentListItem>>> ListAsync(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? search,
        [FromQuery] Guid? tripId,
        [FromQuery] Guid? assigneeId,
        [FromQuery] string? slaState,
        [FromQuery] string? approvalStatus,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var result = await _mediator.Send(
            new ListParcelIncidentsQuery(
                operatorId,
                status,
                type,
                search,
                tripId,
                assigneeId,
                slaState,
                approvalStatus,
                from,
                to,
                page,
                pageSize),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{incidentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ParcelIncidentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelIncidentDetailResponse>> GetDetailAsync(
        Guid incidentId,
        [FromQuery] int? beforeSequence,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetParcelIncidentDetailQuery(incidentId, operatorId, beforeSequence, limit),
            cancellationToken));
    }

    [HttpPost("{incidentId:guid}/mark-found")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelIncidentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelIncidentDetailResponse>> MarkFoundAsync(
        Guid incidentId,
        [FromBody] MarkParcelIncidentFoundRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new MarkIncidentFoundCommand(
                incidentId,
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.ActualLocationType,
                request.ActualLocationId,
                request.LocationSnapshot,
                request.EvidenceReferences,
                request.Note),
            cancellationToken);
        return Ok(await _mediator.Send(
            new GetParcelIncidentDetailQuery(incidentId, operatorId),
            cancellationToken));
    }

    [HttpPost("{incidentId:guid}/resolve")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelIncidentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ParcelIncidentDetailResponse>> ResolveAsync(
        Guid incidentId,
        [FromBody] ResolveParcelIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new ResolveParcelIncidentCommand(
                incidentId,
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.ResolutionCode,
                request.Note),
            cancellationToken);
        return Ok(await _mediator.Send(
            new GetParcelIncidentDetailQuery(incidentId, operatorId),
            cancellationToken));
    }

    [HttpPost("{incidentId:guid}/assign")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelIncidentDetailResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParcelIncidentDetailResponse>> AssignAsync(
        Guid incidentId,
        [FromBody] AssignParcelIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new AssignIncidentSearchTasksCommand(incidentId, operatorId, request.AssigneeUserId),
            cancellationToken);
        return Ok(await _mediator.Send(
            new GetParcelIncidentDetailQuery(incidentId, operatorId),
            cancellationToken));
    }

    [HttpPost("{incidentId:guid}/search-scan")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelIncidentDetailResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParcelIncidentDetailResponse>> RecordSearchResultAsync(
        Guid incidentId,
        [FromBody] RecordParcelSearchResultRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new RecordSearchTaskResultCommand(
                incidentId,
                request.TaskId,
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.Found,
                request.Result,
                request.EvidenceReferences),
            cancellationToken);
        return Ok(await _mediator.Send(
            new GetParcelIncidentDetailQuery(incidentId, operatorId),
            cancellationToken));
    }

    [HttpPost("{incidentId:guid}/forward")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelIncidentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ParcelIncidentDetailResponse>> ForwardAsync(
        Guid incidentId,
        [FromBody] ForwardParcelIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new ForwardIncidentParcelCommand(
                incidentId,
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.TargetTripId),
            cancellationToken);
        return Ok(await _mediator.Send(
            new GetParcelIncidentDetailQuery(incidentId, operatorId),
            cancellationToken));
    }

    [HttpPost("{incidentId:guid}/declare-lost")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelIncidentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ParcelIncidentDetailResponse>> DeclareLostAsync(
        Guid incidentId,
        [FromBody] ResolveParcelIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new DeclareIncidentLostCommand(incidentId, operatorId, CurrentUserClaims.GetUserId(User), request.Note),
            cancellationToken);
        return Ok(await _mediator.Send(
            new GetParcelIncidentDetailQuery(incidentId, operatorId),
            cancellationToken));
    }

    [HttpGet("{incidentId:guid}/forwarding-options")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<IncidentForwardingOptionResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<IncidentForwardingOptionResponse>>> GetForwardingOptionsAsync(
        Guid incidentId,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetIncidentForwardingOptionsQuery(incidentId, operatorId, limit),
            cancellationToken));
    }

    [HttpGet("~/v1/operator/claims")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorParcelClaimListItem>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OperatorParcelClaimListItem>>> ListClaimsAsync(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? slaState,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new ListOperatorParcelClaimsQuery(
                operatorId,
                status,
                search,
                slaState,
                from,
                to,
                page,
                pageSize),
            cancellationToken));
    }

    [HttpGet("~/v1/operator/claims/{claimId:guid}")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [ProducesResponseType(typeof(ApiResponse<OperatorParcelClaimDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperatorParcelClaimDetailResponse>> GetClaimDetailAsync(
        Guid claimId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetOperatorParcelClaimDetailQuery(claimId, operatorId),
            cancellationToken));
    }

    [HttpPost("~/v1/operator/claims/{claimId:guid}/decision")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<OperatorParcelClaimDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OperatorParcelClaimDetailResponse>> DecideClaimAsync(
        Guid claimId,
        [FromBody] DecideParcelClaimRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new DecideParcelClaimCommand(
                claimId,
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.Decision,
                request.ProvenDirectLossVnd,
                request.Reason),
            cancellationToken);
        return Ok(await _mediator.Send(
            new GetOperatorParcelClaimDetailQuery(claimId, operatorId),
            cancellationToken));
    }

    [HttpGet("~/v1/operator/claim-appeals")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ParcelClaimAppealResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ParcelClaimAppealResponse>>> ListClaimAppealsAsync(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new ListParcelClaimAppealsQuery(operatorId, status, page, pageSize),
            cancellationToken));
    }

    [HttpGet("~/v1/operator/claim-appeals/{appealId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ParcelClaimAppealResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelClaimAppealResponse>> GetClaimAppealAsync(
        Guid appealId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetParcelClaimAppealQuery(appealId, operatorId),
            cancellationToken));
    }

    [HttpPost("~/v1/operator/claim-appeals/{appealId:guid}/decision")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelClaimAppealResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ParcelClaimAppealResponse>> DecideClaimAppealAsync(
        Guid appealId,
        [FromBody] DecideParcelClaimAppealRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new DecideParcelClaimAppealCommand(
                appealId,
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.Decision?.Trim().ToUpperInvariant() ?? string.Empty,
                request.RevisedProvenDirectLossVnd,
                request.Reason),
            cancellationToken));
    }

    [HttpPut("~/v1/operator/policies/parcel-compensation")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelCompensationPolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ParcelCompensationPolicyResponse>> UpdatePolicyAsync(
        [FromBody] UpdateParcelCompensationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new UpdateParcelCompensationPolicyCommand(
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.CompensationRatePercent,
                request.MaxCompensationVnd,
                request.NoProofFallbackMultiplier,
                request.ClaimWindowDays,
                request.SearchSlaHours,
                request.DecisionSlaBusinessDays,
                request.PayoutSlaBusinessDays,
                request.BelowDefaultAcknowledged),
            cancellationToken));
    }

    [HttpGet("~/v1/operator/policies/parcel-compensation")]
    [Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
    [ProducesResponseType(typeof(ApiResponse<ParcelCompensationPolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ParcelCompensationPolicyResponse>> GetPolicyAsync(
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetParcelCompensationPolicyQuery(operatorId),
            cancellationToken));
    }
}
