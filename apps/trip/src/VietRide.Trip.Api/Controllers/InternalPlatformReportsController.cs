using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("internal/v1/reports/platform")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalPlatformReportsController : ControllerBase
{
    private readonly ISender _sender;

    public InternalPlatformReportsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("trips")]
    [ProducesResponseType(typeof(PlatformTripReportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlatformTripReportResult>> GetTripsAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetPlatformTripReportQuery(from, to),
            cancellationToken);
        return Ok(result);
    }
}
