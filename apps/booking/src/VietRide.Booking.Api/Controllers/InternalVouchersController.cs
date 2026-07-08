using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Internal.Vouchers;
using VietRide.Booking.Application.Features.Vouchers.AvailableVouchers;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("internal/v1/vouchers")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalVouchersController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISender _sender;

    public InternalVouchersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("validate")]
    [ProducesResponseType(typeof(ApiResponse<InternalValidateVoucherResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate([FromBody] InternalValidateVoucherRequest request, CancellationToken ct)
    {
        var result = await _sender.Send(
            new InternalValidateVoucherCommand(
                request.VoucherCode,
                request.OperatorId,
                request.RouteId,
                request.UserId,
                request.OrderAmount,
                request.Service,
                request.PaymentMethod),
            ct);
        return Ok(result);
    }

    [HttpPost("usages")]
    [ProducesResponseType(typeof(ApiResponse<InternalRecordVoucherUsageResult>), StatusCodes.Status201Created)]
    public async Task<IActionResult> RecordUsage([FromBody] InternalRecordVoucherUsageRequest request, CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var result = await _sender.Send(
            new InternalRecordVoucherUsageCommand(
                request.VoucherId,
                request.UserId,
                request.ReferenceType,
                request.ReferenceId,
                request.DiscountAmount),
            ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpDelete("usages/by-reference")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteUsageByReference(
        [FromQuery] string referenceType,
        [FromQuery] Guid referenceId,
        CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        await _sender.Send(new InternalDeleteVoucherUsageByReferenceCommand(referenceType, referenceId), ct);
        return NoContent();
    }

    [HttpGet("available")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AvailableVoucherItem>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailable(
        [FromQuery] Guid userId,
        [FromQuery] string service,
        [FromQuery] Guid operatorId,
        [FromQuery] Guid routeId,
        [FromQuery] string? paymentMethod,
        [FromQuery] long? orderAmount,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new GetAvailableVouchersQuery(userId, service, null, operatorId, routeId, paymentMethod, orderAmount),
            ct);
        return Ok(result);
    }

    private string GetRequiredIdempotencyKey()
    {
        var value = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            ? values.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Idempotency-Key header is required.");
        }

        return value;
    }
}
