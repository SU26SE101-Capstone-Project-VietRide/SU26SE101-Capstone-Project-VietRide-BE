using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.OperatorReports;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Route("v1/operator/reports")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorReportExportsController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorReportExportsController(ISender sender) => _sender = sender;

    [HttpGet("revenue/export")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> ExportRevenueAsync([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => ExportAsync(OperatorLedgerReportKind.Revenue, from, to, ct);

    [HttpGet("refunds/export")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> ExportRefundsAsync([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => ExportAsync(OperatorLedgerReportKind.Refunds, from, to, ct);

    private async Task<IActionResult> ExportAsync(
        OperatorLedgerReportKind kind,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var operatorId = GetOperatorId();
        var report = await _sender.Send(new ExportOperatorLedgerReportQuery(operatorId, from, to, kind), ct);
        return File(report.Content, report.ContentType, report.FileName, enableRangeProcessing: false);
    }

    private Guid GetOperatorId()
    {
        var value = User.FindFirstValue("operator_id") ?? User.FindFirstValue("operatorId");
        return Guid.TryParse(value, out var operatorId) && operatorId != Guid.Empty
            ? operatorId
            : throw new VietRide.Shared.Application.Exceptions.ForbiddenException("FORBIDDEN", "Operator scope is required.");
    }
}
