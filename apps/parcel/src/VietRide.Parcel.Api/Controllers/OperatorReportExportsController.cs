using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/operator/reports")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorReportExportsController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorReportExportsController(ISender sender) => _sender = sender;

    [HttpGet("parcels/export")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new VietRide.Shared.Application.Exceptions.ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var report = await _sender.Send(new ExportParcelXlsxQuery(operatorId, from, to), ct);
        return File(report.Content, report.ContentType, report.FileName, enableRangeProcessing: false);
    }
}
