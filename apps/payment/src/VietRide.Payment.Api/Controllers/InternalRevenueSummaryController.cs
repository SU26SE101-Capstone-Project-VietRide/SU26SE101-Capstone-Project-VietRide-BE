using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/revenue")]
public sealed class InternalRevenueSummaryController : ControllerBase
{
    private readonly ISender sender;

    public InternalRevenueSummaryController(ISender sender)
    {
        this.sender = sender;
    }

    [HttpGet("admin-summary")]
    [ProducesResponseType(typeof(InternalAdminRevenueSummaryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InternalAdminRevenueSummaryResult>> GetAdminSummaryAsync(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new GetInternalAdminRevenueSummaryQuery(from, to),
            cancellationToken));

    [HttpGet("operators/{operatorId:guid}/summary")]
    [ProducesResponseType(typeof(InternalOperatorRevenueSummaryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InternalOperatorRevenueSummaryResult>> GetOperatorSummaryAsync(
        Guid operatorId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new GetInternalOperatorRevenueSummaryQuery(operatorId, from, to),
            cancellationToken));
}
