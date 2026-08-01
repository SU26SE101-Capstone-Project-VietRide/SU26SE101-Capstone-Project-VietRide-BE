using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.RevenueAnalytics.Admin;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Authorize(Roles = "SYSTEM_ADMIN")]
[Route("v1/admin/revenue/analytics")]
public sealed class AdminRevenueAnalyticsController : ControllerBase
{
    private readonly ISender sender;

    public AdminRevenueAnalyticsController(ISender sender)
    {
        this.sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminRevenueAnalyticsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AdminRevenueAnalyticsResponse>> GetAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? groupBy,
        [FromQuery] int? top,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetAdminRevenueAnalyticsQuery(from, to, groupBy, top),
            cancellationToken);
        return Ok(result);
    }
}
