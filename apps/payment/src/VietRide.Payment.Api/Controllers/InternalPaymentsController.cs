using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Api.Controllers.Requests;
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Web.Authentication;

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

}
