using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Application.Features.Subscriptions.GetOperatorSubscription;
using VietRide.Identity.Application.Features.Subscriptions.ListSubscriptionPlans;
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
        => Ok(await _sender.Send(new ListSubscriptionPlansQuery(false), cancellationToken));

    [HttpPost("upgrade")]
    [IdempotencyOpenApi]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionUpgradeResponseDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
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
                request.ReturnUrl,
                key,
                clientIpAddress),
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
