using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Vouchers.CreateVoucher;
using VietRide.Booking.Application.Features.Vouchers.ListVouchers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

/// <summary>
/// Admin voucher endpoints (SYSTEM_ADMIN only):
/// <list type="bullet">
///   <item>POST /v1/admin/vouchers — create platform voucher + optional OPERATOR_FUNDED consent fan-out.</item>
///   <item>GET /v1/admin/vouchers — oversight list of all vouchers with optional filters (Q7).</item>
/// </list>
/// All responses wrapped in <see cref="ApiResponse{T}"/> by ApiResponseResultFilter (ADR 0004).
/// All errors wrapped by ApiResponseExceptionFilter.
/// </summary>
[ApiController]
[Route("v1/admin/vouchers")]
[Authorize(Roles = SystemAdminRole)]
public sealed class AdminVouchersController : ControllerBase
{
    private const string SystemAdminRole = "SYSTEM_ADMIN";
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly HashSet<string> ValidSortDirs =
        new(StringComparer.OrdinalIgnoreCase) { "asc", "desc" };

    private readonly ISender _sender;

    public AdminVouchersController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Create a platform voucher. For OPERATOR_FUNDED, fans out PENDING consent rows.</summary>
    /// <remarks>
    /// Auth: SYSTEM_ADMIN (RS256 user token via JWKS).
    /// Idempotency-Key header required.
    /// owner_operator_id is always null (platform voucher).
    /// OPERATOR_FUNDED requires applicableOperatorIds non-null/non-empty → 422 VALIDATION_ERROR.
    /// Duplicate code (among non-deleted) → 409 VOUCHER_CODE_CONFLICT.
    /// type: PERCENT_OFF or FIXED_AMOUNT.
    /// fundingType: VIETRIDE_FUNDED or OPERATOR_FUNDED.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateVoucherResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateVoucher(
        [FromBody] CreateVoucherRequest request,
        CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var adminUserId = GetCallerUserId();

        // Enum parsing + validation is delegated to the Application layer
        // (CreateVoucherCommandValidator + handler Enum.Parse) — Api layer stays domain-free.
        var command = new CreateVoucherCommand(
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
            ApplicableOperatorIds: request.ApplicableOperatorIds,
            ApplicableRouteIds: request.ApplicableRouteIds,
            FundingType: request.FundingType,
            CreatedByUserId: adminUserId);

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Admin oversight list of all vouchers (Q7).</summary>
    /// <remarks>
    /// Auth: SYSTEM_ADMIN (RS256 user token via JWKS).
    /// Read-only — no Idempotency-Key required.
    /// Optional filters: ownerOperatorId, fundingType (VIETRIDE_FUNDED | OPERATOR_FUNDED), isActive.
    /// sortBy whitelist: createdAt (default), validFrom, validUntil, code, name, isActive.
    /// Non-whitelisted sortBy → 422 INVALID_SORT_FIELD.
    /// Returns only non-soft-deleted vouchers.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<VoucherListItem>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ListVouchers(
        [FromQuery] ListVouchersRequest request,
        CancellationToken ct)
    {
        // Guard sortDir before constructing QueryOptions — QueryOptions.NormalizeSortDir throws
        // ArgumentException (not CodedValidationException) so it bypasses ApiResponseExceptionFilter.
        // An invalid value must return a proper 422 INVALID_SORT_DIRECTION, not a 500 (BSOT §5.8).
        if (!ValidSortDirs.Contains(request.SortDir))
            throw new VietRide.Shared.Application.Exceptions.CodedValidationException(
                "INVALID_SORT_DIRECTION",
                "sortDir must be 'asc' or 'desc'.");

        // fundingType string → parsed in the query/handler; null stays null (no filter).
        var query = new ListVouchersQuery(
            OwnerOperatorId: request.OwnerOperatorId,
            FundingType: request.FundingType,
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

    private Guid GetCallerUserId()
    {
        var sub = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");

        return userId;
    }
}
