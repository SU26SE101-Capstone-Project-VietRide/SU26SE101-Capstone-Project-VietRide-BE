using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Api.Controllers.Requests;
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;
using VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/payments")]
public sealed class InternalPaymentsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IMediator _mediator;

    public InternalPaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("batch-charge")]
    [ProducesResponseType(typeof(BatchChargePaymentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BatchChargePaymentResult>> BatchChargeAsync(
        [FromBody] BatchChargePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = GetRequiredIdempotencyKey();
        var result = await _mediator.Send(request.ToCommand(idempotencyKey), cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }

    [HttpPost("charge")]
    [ProducesResponseType(typeof(ApiResponse<ChargePaymentResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<ChargePaymentResult>>> ChargeAsync(
        [FromBody] ChargePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = GetRequiredIdempotencyKey();
        var clientIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var result = await _mediator.Send(request.ToCommand(idempotencyKey, clientIpAddress), cancellationToken)
            .ConfigureAwait(false);

        return Ok(ApiResponse<ChargePaymentResult>.Ok(result, ApiMeta.Create(GetTraceId())));
    }

    private string GetRequiredIdempotencyKey()
    {
        var value = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            ? values.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Idempotency-Key header is required.");
        }

        return value;
    }

    private string GetTraceId()
    {
        if (HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var id)
            && id is string traceId
            && !string.IsNullOrWhiteSpace(traceId))
        {
            return traceId;
        }

        return Request.Headers.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var values)
            ? values.ToString()
            : string.Empty;
    }
}
