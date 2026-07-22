using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.OperatorReports;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/operator/reports")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorReportExportsController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorReportExportsController(ISender sender) => _sender = sender;

    [HttpGet("bookings/export")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> ExportBookingsAsync([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => ExportAsync(BookingOperatorReportKind.Bookings, from, to, ct);

    [HttpGet("cancellation/export")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> ExportCancellationsAsync([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => ExportAsync(BookingOperatorReportKind.Cancellations, from, to, ct);

    private async Task<IActionResult> ExportAsync(
        BookingOperatorReportKind kind,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var operatorId = GetOperatorId();
        var report = await _sender.Send(new ExportBookingReportQuery(operatorId, from, to, kind), ct);
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
