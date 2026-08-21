using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Api.Filters;
using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Application.Features.Subscriptions.ConfirmSubscriptionUpgradePayment;
using VietRide.Identity.Application.Features.Subscriptions.CustomRequests;
using VietRide.Identity.Application.Features.Subscriptions.GetOperatorSubscription;
using VietRide.Identity.Application.Features.Subscriptions.ListSubscriptionPlans;
using VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;
using VietRide.Identity.Application.Features.Subscriptions.RetrySubscriptionPayment;
using VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/operator/subscription")]
[Authorize(Roles = "OPERATOR_ADMIN")]
[ServiceFilter(typeof(SubscriptionUniqueConstraintExceptionFilter))]
public sealed class OperatorSubscriptionController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private readonly ISender _sender;

    public OperatorSubscriptionController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OperatorSubscriptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperatorSubscriptionDto>> GetAsync(CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        return Ok(await _sender.Send(new GetOperatorSubscriptionQuery(operatorId), cancellationToken));
    }

    [HttpGet("/v1/operator/subscription-plans")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionPlanDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        return Ok(await _sender.Send(new ListSubscriptionPlansQuery(false, operatorId), cancellationToken));
    }

    [HttpPost("custom-requests")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionCustomRequestDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubscriptionCustomRequestDto>> CreateCustomRequestAsync(
        [FromBody] SubscriptionCustomRequestRequest request,
        CancellationToken cancellationToken)
    {
        _ = GetRequiredIdempotencyKey();
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        var result = await _sender.Send(
            request.ToCommand(CurrentUserClaims.GetUserId(User), operatorId),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("custom-requests")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubscriptionCustomRequestDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionCustomRequestDto>>> ListCustomRequestsAsync(
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        return Ok(await _sender.Send(new ListSubscriptionCustomRequestsQuery(operatorId), cancellationToken));
    }

    [HttpGet("custom-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionCustomRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionCustomRequestDto>> GetCustomRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        return Ok(await _sender.Send(new GetSubscriptionCustomRequestQuery(requestId, operatorId), cancellationToken));
    }

    [HttpPost("upgrade")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeResponseDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SubscriptionUpgradeResponseDto>> UpgradeAsync(
        [FromBody] SubscriptionUpgradeRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        var key = GetRequiredIdempotencyKey();
        var clientIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var result = await _sender.Send(
            new UpgradeSubscriptionCommand(
                operatorId,
                request.PlanId,
                request.BillingPeriod,
                request.PaymentMethod,
                key,
                clientIpAddress),
            cancellationToken);

        return StatusCode(
            result.Status == SubscriptionStatus.ACTIVE.ToString()
                ? StatusCodes.Status200OK
                : StatusCodes.Status202Accepted,
            result);
    }

    [HttpPost("upgrade/quote")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeQuoteDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubscriptionUpgradeQuoteDto>> QuoteAsync(
        [FromBody] SubscriptionUpgradeRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        var result = await _sender.Send(
            new QuoteSubscriptionUpgradeCommand(
                operatorId,
                request.PlanId,
                request.BillingPeriod,
                request.PaymentMethod,
                GetRequiredIdempotencyKey()),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("upgrade/{upgradeAttemptId:guid}/payment")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeResponseDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SubscriptionUpgradeResponseDto>> ConfirmPaymentAsync(
        Guid upgradeAttemptId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        var result = await _sender.Send(
            new ConfirmSubscriptionUpgradePaymentCommand(
                operatorId,
                upgradeAttemptId,
                GetRequiredIdempotencyKey(),
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1"),
            cancellationToken);
        return StatusCode(
            result.Status == SubscriptionStatus.ACTIVE.ToString()
                ? StatusCodes.Status200OK
                : StatusCodes.Status202Accepted,
            result);
    }

    [HttpPost("upgrade/{upgradeAttemptId:guid}/retry-payment")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeResponseDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SubscriptionUpgradeResponseDto>> RetryPaymentAsync(
        Guid upgradeAttemptId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "The authenticated user is not scoped to an operator.");
        var result = await _sender.Send(
            new RetrySubscriptionPaymentCommand(
                operatorId,
                upgradeAttemptId,
                GetRequiredIdempotencyKey(),
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1"),
            cancellationToken);
        return StatusCode(StatusCodes.Status202Accepted, result);
    }

    private string GetRequiredIdempotencyKey()
    {
        var value = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values) ? values.ToString() : string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            throw new CodedValidationException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");

        return value;
    }
}
