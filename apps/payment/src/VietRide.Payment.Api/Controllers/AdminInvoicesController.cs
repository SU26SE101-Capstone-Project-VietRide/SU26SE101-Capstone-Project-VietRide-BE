using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Route("v1/admin/invoices")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminInvoicesController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private readonly ISender _sender;

    public AdminInvoicesController(ISender sender) => _sender = sender;

    /// <summary>Requeue a retryable failed invoice PDF generation.</summary>
    [HttpPost("{invoiceId:guid}/retry")]
    [ProducesResponseType(typeof(ApiResponse<RetryInvoiceResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RetryInvoiceResult>> Retry(
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            || !values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            throw new CodedValidationException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency-Key header is required.");
        }

        var result = await _sender.Send(new RetryInvoiceCommand(invoiceId), cancellationToken);
        return StatusCode(StatusCodes.Status202Accepted, result);
    }
}
