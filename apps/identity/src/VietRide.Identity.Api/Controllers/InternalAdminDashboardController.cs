using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Application.Features.Internal.AdminDashboard;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/admin/dashboard")]
public sealed class InternalAdminDashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalAdminDashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("identity-metrics")]
    [ProducesResponseType(typeof(AdminDashboardIdentityMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminDashboardIdentityMetricsResponse>> GetIdentityMetricsAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAdminDashboardIdentityMetricsQuery(from, to),
            cancellationToken);
        return Ok(result);
    }
}
