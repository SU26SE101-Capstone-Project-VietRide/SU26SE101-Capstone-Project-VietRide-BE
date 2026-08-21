using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Api.Filters;
using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Application.Features.Subscriptions.CustomRequests;
using VietRide.Identity.Application.Features.Subscriptions.ListSubscriptionPlans;
using VietRide.Identity.Application.Features.Subscriptions.ManageSubscriptionPlan;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/admin/subscription-plans")]
[Authorize(Roles = "SYSTEM_ADMIN")]
[ServiceFilter(typeof(SubscriptionUniqueConstraintExceptionFilter))]
public sealed class AdminSubscriptionPlansController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private readonly ISender _sender;

    public AdminSubscriptionPlansController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionPlanDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> ListAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(new ListSubscriptionPlansQuery(includeInactive), cancellationToken));

    [HttpPost]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionPlanDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<SubscriptionPlanDto>> CreateAsync(
        [FromBody] SubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey();
        var result = await _sender.Send(
            request.ToCommand(null, CurrentUserClaims.GetUserId(User)),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{planId:guid}")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionPlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubscriptionPlanDto>> UpdateAsync(
        Guid planId,
        [FromBody] SubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey();
        return Ok(await _sender.Send(
            request.ToCommand(planId, CurrentUserClaims.GetUserId(User)),
            cancellationToken));
    }

    [HttpGet("custom-requests")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionCustomRequestDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionCustomRequestDto>>> ListCustomRequestsAsync(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(new ListSubscriptionCustomRequestsQuery(null, status), cancellationToken));

    [HttpGet("custom-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionCustomRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionCustomRequestDto>> GetCustomRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
        => Ok(await _sender.Send(new GetSubscriptionCustomRequestQuery(requestId, null), cancellationToken));

    [HttpPost("custom-requests/{requestId:guid}/approve")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionCustomRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubscriptionCustomRequestDto>> ApproveCustomRequestAsync(
        Guid requestId,
        [FromBody] ApproveSubscriptionCustomRequestRequest request,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey();
        return Ok(await _sender.Send(
            request.ToCommand(CurrentUserClaims.GetUserId(User), requestId),
            cancellationToken));
    }

    [HttpPost("custom-requests/{requestId:guid}/reject")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionCustomRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubscriptionCustomRequestDto>> RejectCustomRequestAsync(
        Guid requestId,
        [FromBody] RejectSubscriptionCustomRequestRequest request,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey();
        return Ok(await _sender.Send(
            new RejectSubscriptionCustomRequestCommand(
                CurrentUserClaims.GetUserId(User),
                requestId,
                request.Reason),
            cancellationToken));
    }

    private void RequireIdempotencyKey()
    {
        if (!Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values) || string.IsNullOrWhiteSpace(values.ToString()))
            throw new CodedValidationException("VALIDATION_ERROR", "Idempotency-Key header is required.");
    }
}
