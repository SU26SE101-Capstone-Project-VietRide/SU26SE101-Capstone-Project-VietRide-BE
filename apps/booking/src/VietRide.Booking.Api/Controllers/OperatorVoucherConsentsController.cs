using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.VoucherConsents.AcceptVoucherConsent;
using VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;
using VietRide.Booking.Application.Features.VoucherConsents.RejectVoucherConsent;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

/// <summary>
/// Operator voucher-consent endpoints:
/// <list type="bullet">
///   <item>GET /v1/operator/voucher-consents — list operator-scoped consents (OPERATOR_ADMIN/STAFF).</item>
///   <item>POST /v1/operator/voucher-consents/{id}/accept — accept a PENDING consent (OPERATOR_ADMIN only).</item>
///   <item>POST /v1/operator/voucher-consents/{id}/reject — reject/revoke a consent (OPERATOR_ADMIN only).</item>
/// </list>
/// All responses wrapped in <see cref="ApiResponse{T}"/> by ApiResponseResultFilter (ADR 0004).
/// All errors wrapped by ApiResponseExceptionFilter.
/// </summary>
[ApiController]
[Route("v1/operator/voucher-consents")]
[Authorize]
public sealed class OperatorVoucherConsentsController : ControllerBase
{
    private const string OperatorAdminRole = "OPERATOR_ADMIN";
    private const string OperatorStaffRole = "OPERATOR_STAFF";
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISender _sender;

    public OperatorVoucherConsentsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>List operator-scoped voucher consents, optionally filtered by status.</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN or OPERATOR_STAFF (RS256 user token via JWKS).
    /// Tenant isolation: only consents for the caller's operatorId are returned.
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = $"{OperatorAdminRole},{OperatorStaffRole}")]
    [ProducesResponseType(typeof(ApiResponse<ListVoucherConsentsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ListConsents(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var (_, callerOperatorId) = GetCallerIds();

        // Enum parsing + validation delegated to ListVoucherConsentsQueryHandler
        // so the Api layer stays domain-free (no VietRide.Booking.Domain reference).
        var query = new ListVoucherConsentsQuery(
            CallerOperatorId: callerOperatorId,
            Status: status);

        var result = await _sender.Send(query, ct);
        return Ok(result);
    }

    /// <summary>Accept a PENDING voucher-consent (PENDING → ACCEPTED).</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN only (RS256 user token via JWKS).
    /// Idempotency-Key header required.
    /// Precondition: consent status = PENDING — otherwise 409 CONSENT_NOT_PENDING.
    /// Emits booking.voucher.consent_accepted via Outbox.
    /// Cross-operator access → 403 FORBIDDEN.
    /// </remarks>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Roles = OperatorAdminRole)]
    [ProducesResponseType(typeof(ApiResponse<AcceptVoucherConsentResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptConsent(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var (callerUserId, callerOperatorId) = GetCallerIds();

        var command = new AcceptVoucherConsentCommand(
            ConsentId: id,
            CallerOperatorId: callerOperatorId,
            CallerUserId: callerUserId);

        var result = await _sender.Send(command, ct);
        return Ok(result);
    }

    /// <summary>Reject or revoke a voucher-consent (PENDING|ACCEPTED → REJECTED).</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN only (RS256 user token via JWKS).
    /// Idempotency-Key header required.
    /// Precondition: consent status IN (PENDING, ACCEPTED) — otherwise 409 CONSENT_ALREADY_REJECTED.
    /// Revoking an ACCEPTED consent does NOT roll back discounts on already-CONFIRMED bookings.
    /// Emits booking.voucher.consent_rejected via Outbox.
    /// Cross-operator access → 403 FORBIDDEN.
    /// </remarks>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = OperatorAdminRole)]
    [ProducesResponseType(typeof(ApiResponse<RejectVoucherConsentResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectConsent(
        [FromRoute] Guid id,
        [FromBody] RejectVoucherConsentRequest? request,
        CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var (callerUserId, callerOperatorId) = GetCallerIds();

        var command = new RejectVoucherConsentCommand(
            ConsentId: id,
            CallerOperatorId: callerOperatorId,
            CallerUserId: callerUserId,
            Reason: request?.Reason);

        var result = await _sender.Send(command, ct);
        return Ok(result);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private string GetRequiredIdempotencyKey()
    {
        var value = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            ? values.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            throw new VietRide.Shared.Application.Exceptions.CodedValidationException(
                "VALIDATION_ERROR",
                "Idempotency-Key header is required.");

        return value;
    }

    private (Guid UserId, Guid OperatorId) GetCallerIds()
    {
        var sub = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");

        var operatorIdStr = User.FindFirstValue("operatorId");
        if (!Guid.TryParse(operatorIdStr, out var operatorId))
            throw new UnauthorizedAccessException("Authenticated caller operatorId claim is missing or invalid.");

        return (userId, operatorId);
    }
}
