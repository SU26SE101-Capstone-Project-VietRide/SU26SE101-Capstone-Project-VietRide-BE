using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Route("internal/v1/reports/platform")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalPlatformReportAggregateController : ControllerBase
{
    private readonly ISender _sender;

    public InternalPlatformReportAggregateController(ISender sender) => _sender = sender;

    [HttpGet("aggregate")]
    [ProducesResponseType(typeof(PlatformReportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlatformReportResult>> GetAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
        => Ok(await _sender.Send(new GetPlatformReportQuery(from, to), ct));

    [HttpGet("ledger")]
    [ProducesResponseType(typeof(PlatformLedgerReportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlatformLedgerReportResult>> GetLedgerAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
        => Ok(await _sender.Send(new GetPlatformLedgerReportQuery(from, to), ct));
}
