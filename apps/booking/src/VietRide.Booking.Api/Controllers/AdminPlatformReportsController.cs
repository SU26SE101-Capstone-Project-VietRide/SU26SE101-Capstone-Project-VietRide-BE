using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/admin/reports/platform")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminPlatformReportsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminPlatformReportsController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PlatformReportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlatformReportResult>> GetAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
        => Ok(await _sender.Send(new GetPlatformReportQuery(from, to), ct));
}
