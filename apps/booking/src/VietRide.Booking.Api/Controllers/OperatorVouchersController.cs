using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.OperatorVouchers.CreateOperatorVoucher;
using VietRide.Booking.Application.Features.OperatorVouchers.DeleteOperatorVoucher;
using VietRide.Booking.Application.Features.OperatorVouchers.SetOperatorVoucherActive;
using VietRide.Booking.Application.Features.OperatorVouchers.UpdateOperatorVoucher;
using VietRide.Booking.Application.Features.Vouchers.ListVouchers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

/// <summary>
/// Operator self-service voucher CRUD endpoints (OPERATOR_ADMIN only):
/// <list type="bullet">
///   <item>POST /v1/operator/vouchers — create an operator-owned OPERATOR_FUNDED voucher.</item>
///   <item>GET /v1/operator/vouchers — list vouchers owned by the caller operator.</item>
///   <item>PATCH /v1/operator/vouchers/{id} — partial update (freeze-on-first-use, Q6).</item>
///   <item>DELETE /v1/operator/vouchers/{id} — soft-delete (sets deleted_at, ADR 0003).</item>
///   <item>POST /v1/operator/vouchers/{id}/activate — flip IsActive = true.</item>
///   <item>POST /v1/operator/vouchers/{id}/deactivate — flip IsActive = false.</item>
/// </list>
/// All responses wrapped in <see cref="ApiResponse{T}"/> by ApiResponseResultFilter (ADR 0004).
/// All errors wrapped by ApiResponseExceptionFilter.
/// </summary>
[ApiController]
[Route("v1/operator/vouchers")]
[Authorize(Roles = OperatorAdminRole)]
public sealed class OperatorVouchersController : ControllerBase
{
    private const string OperatorAdminRole = "OPERATOR_ADMIN";
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly HashSet<string> ValidSortDirs =
        new(StringComparer.OrdinalIgnoreCase) { "asc", "desc" };

    private readonly ISender _sender;

    public OperatorVouchersController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Create an operator-owned OPERATOR_FUNDED voucher (self-consented, no consent fan-out).</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN (RS256 user token via JWKS).
    /// Idempotency-Key header required.
    /// fundingType is FORCED to OPERATOR_FUNDED — any other value → 422 VOUCHER_FORBIDDEN_FUNDING.
    /// applicableOperatorIds is FORCED to [callerOperatorId] — self-consented.
    /// Duplicate global code → 409 VOUCHER_CODE_CONFLICT.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateOperatorVoucherResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateVoucher(
        [FromBody] CreateOperatorVoucherRequest request,
        CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var (callerUserId, callerOperatorId) = GetCallerIds();

        var command = new CreateOperatorVoucherCommand(
            Code: request.Code,
            Name: request.Name,
            Type: request.Type,
            Value: request.Value,
            MinOrderAmount: request.MinOrderAmount,
            MaxDiscountAmount: request.MaxDiscountAmount,
            TotalUsageLimit: request.TotalUsageLimit,
            PerUserLimit: request.PerUserLimit,
            ValidFrom: request.ValidFrom,
            ValidUntil: request.ValidUntil,
            ApplicableServices: request.ApplicableServices,
            ApplicableRouteIds: request.ApplicableRouteIds,
            FundingType: request.FundingType,
            OwnerOperatorId: callerOperatorId,
            CreatedByUserId: callerUserId);

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>List vouchers owned by the caller operator.</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN (RS256 user token via JWKS).
    /// ownerOperatorId is always taken from the JWT operatorId claim and never from query string.
    /// Optional filters: isActive. sortBy whitelist is validated by Application.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<VoucherListItem>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ListVouchers(
        [FromQuery] ListOperatorVouchersRequest request,
        CancellationToken ct)
    {
        if (!ValidSortDirs.Contains(request.SortDir))
            throw new VietRide.Shared.Application.Exceptions.CodedValidationException(
                "INVALID_SORT_DIRECTION",
                "sortDir must be 'asc' or 'desc'.");

        var (_, callerOperatorId) = GetCallerIds();

        var query = new ListVouchersQuery(
            OwnerOperatorId: callerOperatorId,
            PlatformOnly: false,
            FundingType: null,
            IsActive: request.IsActive,
            Options: new QueryOptions
            {
                Page = request.Page,
                PageSize = request.PageSize,
                SortBy = request.SortBy,
                SortDir = request.SortDir,
            });

        var result = await _sender.Send(query, ct);

        return Ok(result);
    }

    /// <summary>Partial update of operator-owned voucher fields (freeze-on-first-use, Q6).</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN (RS256 user token via JWKS). No Idempotency-Key.
    /// code, type, fundingType, ownerOperatorId are ALWAYS immutable.
    /// Before the first usage, all request fields are editable.
    /// Once ≥1 usage exists: value, minOrderAmount, maxDiscountAmount, and validFrom are frozen;
    /// validUntil may only be extended, usage limits may only be loosened, while name,
    /// applicableRouteIds, and deactivate remain editable. Invalid locked edits return 409 VOUCHER_LOCKED.
    /// Cross-operator access → 404 VOUCHER_NOT_FOUND.
    /// </remarks>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UpdateOperatorVoucherResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateVoucher(
        [FromRoute] Guid id,
        [FromBody] UpdateOperatorVoucherRequest request,
        CancellationToken ct)
    {
        var (_, callerOperatorId) = GetCallerIds();

        var command = new UpdateOperatorVoucherCommand(
            VoucherId: id,
            CallerOperatorId: callerOperatorId,
            Name: request.Name,
            Value: request.Value,
            MinOrderAmount: request.MinOrderAmount,
            MaxDiscountAmount: request.MaxDiscountAmount,
            TotalUsageLimit: request.TotalUsageLimit,
            PerUserLimit: request.PerUserLimit,
            ValidFrom: request.ValidFrom,
            ValidUntil: request.ValidUntil,
            ApplicableRouteIds: request.ApplicableRouteIds);  // All nullable — null = keep current

        var result = await _sender.Send(command, ct);

        return Ok(result);
    }

    /// <summary>Soft-deletes an operator-owned voucher (sets deleted_at; code becomes reusable).</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN (RS256 user token via JWKS). No Idempotency-Key.
    /// Idempotent: deleting an already soft-deleted voucher returns the existing deletedAt timestamp.
    /// Cross-operator access → 404 VOUCHER_NOT_FOUND.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DeleteOperatorVoucherResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVoucher(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var (_, callerOperatorId) = GetCallerIds();

        var command = new DeleteOperatorVoucherCommand(
            VoucherId: id,
            CallerOperatorId: callerOperatorId);

        var result = await _sender.Send(command, ct);

        return Ok(result);
    }

    /// <summary>Activate an operator-owned voucher (sets IsActive = true). Behavior-idempotent.</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN (RS256 user token via JWKS). No Idempotency-Key.
    /// Cross-operator access → 404 VOUCHER_NOT_FOUND.
    /// </remarks>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(ApiResponse<SetOperatorVoucherActiveResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateVoucher(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var (_, callerOperatorId) = GetCallerIds();

        var command = new SetOperatorVoucherActiveCommand(
            VoucherId: id,
            CallerOperatorId: callerOperatorId,
            Activate: true);

        var result = await _sender.Send(command, ct);

        return Ok(result);
    }

    /// <summary>Deactivate an operator-owned voucher (sets IsActive = false). Behavior-idempotent.</summary>
    /// <remarks>
    /// Auth: OPERATOR_ADMIN (RS256 user token via JWKS). No Idempotency-Key.
    /// Cross-operator access → 404 VOUCHER_NOT_FOUND.
    /// </remarks>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(ApiResponse<SetOperatorVoucherActiveResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateVoucher(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var (_, callerOperatorId) = GetCallerIds();

        var command = new SetOperatorVoucherActiveCommand(
            VoucherId: id,
            CallerOperatorId: callerOperatorId,
            Activate: false);

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
