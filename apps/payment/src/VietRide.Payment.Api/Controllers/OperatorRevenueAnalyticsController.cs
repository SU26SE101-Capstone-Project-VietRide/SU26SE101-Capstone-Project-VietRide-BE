using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.RevenueAnalytics.Operator;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Authorize(Roles = "OPERATOR_ADMIN")]
[Route("v1/operator/revenue/analytics")]
public sealed class OperatorRevenueAnalyticsController : ControllerBase
{
    private readonly ISender sender;

    public OperatorRevenueAnalyticsController(ISender sender)
    {
        this.sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OperatorRevenueAnalyticsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OperatorRevenueAnalyticsResponse>> GetAsync(
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetOperatorRevenueAnalyticsQuery(GetOperatorId(), month),
            cancellationToken);
        return Ok(result);
    }

    private Guid GetOperatorId()
    {
        var value = User.FindFirstValue("operator_id") ?? User.FindFirstValue("operatorId");
        return Guid.TryParse(value, out var operatorId) && operatorId != Guid.Empty
            ? operatorId
            : throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
    }
}
