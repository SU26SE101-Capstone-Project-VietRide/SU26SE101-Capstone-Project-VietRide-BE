using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Api.Controllers.Requests;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/wallet")]
public sealed class InternalWalletController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISender _sender;

    public InternalWalletController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("refund")]
    [ProducesResponseType(typeof(ApiResponse<RefundToWalletResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<RefundToWalletResult>>> RefundAsync(
        [FromBody] RefundToWalletRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = GetRequiredIdempotencyKey();
        var result = await _sender.Send(request.ToCommand(idempotencyKey), cancellationToken)
            .ConfigureAwait(false);

        return Ok(ApiResponse<RefundToWalletResult>.Ok(result, ApiMeta.Create(GetTraceId())));
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
