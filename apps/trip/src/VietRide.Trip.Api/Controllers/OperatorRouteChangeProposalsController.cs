using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.RouteChangeProposals;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/route-change-proposals")]
[Authorize(Roles = "OPERATOR_ADMIN")]
public sealed class OperatorRouteChangeProposalsController : ControllerBase
{
    private readonly IMediator mediator;
    public OperatorRouteChangeProposalsController(IMediator mediator) => this.mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RouteChangeProposalDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<RouteChangeProposalDto>>> ListAsync(
        [FromQuery] Guid? tripId,
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListOperatorRouteChangeProposalsQuery(GetRequiredOperatorId(), tripId, status, type, page, pageSize), cancellationToken));

    [HttpGet("{proposalId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<RouteChangeProposalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteChangeProposalDto>> GetAsync(Guid proposalId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetOperatorRouteChangeProposalQuery(GetRequiredOperatorId(), proposalId), cancellationToken));

    [HttpGet("{proposalId}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult RejectMalformedGetProposalId(string proposalId)
        => throw InvalidProposalId();

    [HttpPost("{proposalId:guid}/approve")]
    [RequireIdempotency(AllowRequestBody = false)]
    [ProducesResponseType(typeof(ApiResponse<ApproveRouteChangeProposalResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApproveRouteChangeProposalResponse>> ApproveAsync(Guid proposalId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ApproveRouteChangeProposalCommand(GetRequiredOperatorId(), CurrentUserClaims.GetUserId(User), proposalId), cancellationToken));

    [HttpPost("{proposalId}/approve")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [RequireIdempotency(AllowRequestBody = false)]
    public ActionResult RejectMalformedApproveProposalId(string proposalId)
        => throw InvalidProposalId();

    [HttpPost("{proposalId:guid}/reject")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<RouteChangeProposalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RouteChangeProposalDto>> RejectAsync(Guid proposalId, [FromBody] RejectRouteChangeProposalRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new RejectRouteChangeProposalCommand(GetRequiredOperatorId(), CurrentUserClaims.GetUserId(User), proposalId, request.Reason), cancellationToken));

    [HttpPost("{proposalId}/reject")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [RequireIdempotency]
    public ActionResult RejectMalformedRejectProposalId(string proposalId)
        => throw InvalidProposalId();

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User) ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage route-change proposals.");

    private static CodedValidationException InvalidProposalId()
        => new(
            "VALIDATION_ERROR",
            "proposalId must be a valid non-empty UUID.",
            [new ValidationError("proposalId", "proposalId must be a valid non-empty UUID.")]);
}
