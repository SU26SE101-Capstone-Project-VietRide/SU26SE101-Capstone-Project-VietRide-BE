using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Application.Features.Subscriptions.ListSubscriptionPlans;
using VietRide.Identity.Application.Features.Subscriptions.ManageSubscriptionPlan;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/admin/subscription-plans")]
[Authorize(Roles = "SYSTEM_ADMIN")]
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
    [ProducesResponseType(typeof(ApiResponse<SubscriptionPlanDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<SubscriptionPlanDto>> CreateAsync(
        [FromBody] SubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey();
        var result = await _sender.Send(request.ToCommand(null), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{planId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionPlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubscriptionPlanDto>> UpdateAsync(
        Guid planId,
        [FromBody] SubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey();
        return Ok(await _sender.Send(request.ToCommand(planId), cancellationToken));
    }

    private void RequireIdempotencyKey()
    {
        if (!Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values) || string.IsNullOrWhiteSpace(values.ToString()))
            throw new CodedValidationException("VALIDATION_ERROR", "Idempotency-Key header is required.");
    }
}
