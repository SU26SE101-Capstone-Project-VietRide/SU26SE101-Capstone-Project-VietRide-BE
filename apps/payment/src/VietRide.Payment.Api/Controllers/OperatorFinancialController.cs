using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Management;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Route("v1/operator")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorFinancialController : ControllerBase
{
    private readonly ISender _sender;
    public OperatorFinancialController(ISender sender) => _sender = sender;

    [HttpGet("wallet")]
    [ProducesResponseType(typeof(ApiResponse<OperatorWalletDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<OperatorWalletDto>> Wallet(CancellationToken ct)
        => Ok(await _sender.Send(new GetOperatorWalletQuery(GetOperatorId()), ct));

    [HttpGet("wallet/transactions")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<WalletTransactionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WalletTransactionDto>>> Transactions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null, [FromQuery] string? referenceType = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? search = null, [FromQuery] string? dateField = null,
        [FromQuery] string? sortBy = null, [FromQuery] string sortDir = "desc",
        CancellationToken ct = default)
        => Ok(await _sender.Send(new ListOperatorTransactionsQuery(GetOperatorId(),
            new PageOptions(page, pageSize, sortBy, sortDir, from, to), type, referenceType, search, dateField), ct));

    [HttpGet("trip-settlements")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SettlementDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SettlementDto>>> Settlements(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, [FromQuery] Guid? tripId = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? search = null, [FromQuery] string? dateField = null,
        [FromQuery] string? sortBy = null, [FromQuery] string sortDir = "desc",
        CancellationToken ct = default)
        => Ok(await _sender.Send(new ListOperatorSettlementsQuery(GetOperatorId(),
            new PageOptions(page, pageSize, sortBy, sortDir, from, to), status, tripId, search, dateField), ct));

    [HttpGet("ledger")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<LedgerEntryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LedgerEntryDto>>> Ledger(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] Guid? tripId = null, [FromQuery] string? entryType = null,
        [FromQuery] string? referenceType = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? search = null, [FromQuery] string? dateField = null,
        [FromQuery] string? sortBy = null, [FromQuery] string sortDir = "desc",
        CancellationToken ct = default)
        => Ok(await _sender.Send(new ListOperatorLedgerQuery(GetOperatorId(),
            new PageOptions(page, pageSize, sortBy, sortDir, from, to), tripId, entryType, referenceType, search, dateField), ct));

    [HttpGet("wallet/reconciliation/export")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExportWalletReconciliation(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var report = await _sender.Send(new ExportOperatorWalletReconciliationQuery(
            GetOperatorId(),
            from,
            to), ct);
        return File(report.Content, report.ContentType, report.FileName, enableRangeProcessing: false);
    }

    private Guid GetOperatorId()
    {
        var value = User.FindFirstValue("operatorId") ?? User.FindFirstValue("operator_id");
        if (Guid.TryParse(value, out var operatorId))
            return operatorId;
        throw new VietRide.Shared.Application.Exceptions.ForbiddenException(
            "FORBIDDEN",
            "Operator scope is required.");
    }
}
