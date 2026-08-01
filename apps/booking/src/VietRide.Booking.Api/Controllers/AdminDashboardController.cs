using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.Admin.Dashboard;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/admin/dashboard")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminDashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminDashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AdminDashboardSummaryResponse>> GetSummaryAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminDashboardSummaryQuery(from, to),
            cancellationToken);
        return Ok(result);
    }
}
