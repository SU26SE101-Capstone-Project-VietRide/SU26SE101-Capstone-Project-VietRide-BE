using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Management;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Route("v1/admin")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminFinancialController : ControllerBase
{
    private readonly ISender _sender;
    public AdminFinancialController(ISender sender) => _sender = sender;

    [HttpGet("trip-settlements")]
    [AllowedQueryParameters("page", "pageSize", "operatorId", "status", "tripId", "stuckOnly", "severity", "from", "to", "sortBy", "sortDir", "search")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminSettlementDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminSettlementDto>>> Settlements(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] Guid? operatorId = null, [FromQuery] string? status = null,
        [FromQuery] Guid? tripId = null, [FromQuery] bool stuckOnly = false,
        [FromQuery] string? severity = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? sortBy = null, [FromQuery] string sortDir = "desc",
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new ListAdminSettlementsQuery(
            new PageOptions(page, pageSize, sortBy, sortDir, from, to), operatorId, status, tripId, stuckOnly, severity, search), ct));

    [HttpPost("trip-settlements/{settlementId:guid}/settle")]
    [ProducesResponseType(typeof(ApiResponse<ManualSettlementResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ManualSettlementResult>> Settle(Guid settlementId, CancellationToken ct)
    {
        RequireIdempotencyKey();
        return Ok(await _sender.Send(new SettleTripManuallyCommand(settlementId, GetUserId()), ct));
    }

    [HttpGet("platform-wallet")]
    [ProducesResponseType(typeof(ApiResponse<PlatformWalletDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlatformWalletDto>> PlatformWallet(CancellationToken ct)
        => Ok(await _sender.Send(new GetPlatformWalletQuery(), ct));

    [HttpGet("platform-wallet/transactions")]
    [AllowedQueryParameters("page", "pageSize", "type", "referenceType", "operatorId", "tripId", "businessGroup", "cashFlowPurpose", "from", "to", "sortBy", "sortDir", "search")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PlatformWalletTransactionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PlatformWalletTransactionDto>>> PlatformTransactions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null, [FromQuery] string? referenceType = null,
        [FromQuery] Guid? operatorId = null, [FromQuery] Guid? tripId = null,
        [FromQuery] string? businessGroup = null, [FromQuery] string? cashFlowPurpose = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? sortBy = null, [FromQuery] string sortDir = "desc",
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new ListPlatformTransactionsQuery(
            new PageOptions(page, pageSize, sortBy, sortDir, from, to), type, referenceType, search,
            operatorId, tripId, businessGroup, cashFlowPurpose), ct));

    [HttpGet("platform-wallet/reconciliation-summary")]
    [AllowedQueryParameters("from", "to")]
    [ProducesResponseType(typeof(ApiResponse<PlatformWalletReconciliationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlatformWalletReconciliationSummaryDto>> PlatformReconciliationSummary(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
        => Ok(await _sender.Send(new GetPlatformWalletReconciliationSummaryQuery(from, to), ct));

    [HttpGet("platform-wallet/transactions/export")]
    [AllowedQueryParameters("type", "referenceType", "operatorId", "tripId", "businessGroup", "cashFlowPurpose", "from", "to", "sortBy", "sortDir", "search")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExportPlatformTransactions(
        [FromQuery] string? type = null,
        [FromQuery] string? referenceType = null,
        [FromQuery] Guid? operatorId = null,
        [FromQuery] Guid? tripId = null,
        [FromQuery] string? businessGroup = null,
        [FromQuery] string? cashFlowPurpose = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortDir = "desc",
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var report = await _sender.Send(new ExportPlatformTransactionsQuery(
            new PageOptions(1, 100, sortBy, sortDir, from, to),
            type,
            referenceType,
            search,
            operatorId,
            tripId,
            businessGroup,
            cashFlowPurpose), ct);
        return File(report.Content, report.ContentType, report.FileName, enableRangeProcessing: false);
    }

    [HttpPost("platform-wallet/adjust")]
    [ProducesResponseType(typeof(ApiResponse<AdjustmentResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdjustmentResult>> AdjustPlatform([FromBody] AdjustmentRequest request, CancellationToken ct)
    {
        RequireIdempotencyKey();
        return Ok(await _sender.Send(new AdjustPlatformWalletCommand(request, GetUserId()), ct));
    }

    [HttpPost("operators/{operatorId:guid}/wallet/adjust")]
    [ProducesResponseType(typeof(ApiResponse<AdjustmentResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdjustmentResult>> AdjustOperator(
        Guid operatorId, [FromBody] AdjustmentRequest request, CancellationToken ct)
    {
        RequireIdempotencyKey();
        return Ok(await _sender.Send(new AdjustOperatorWalletCommand(operatorId, request, GetUserId()), ct));
    }

    private void RequireIdempotencyKey()
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var values)
            || !values.Any(value => !string.IsNullOrWhiteSpace(value)))
            throw new CodedValidationException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out var userId))
            return userId;
        throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");
    }
}
