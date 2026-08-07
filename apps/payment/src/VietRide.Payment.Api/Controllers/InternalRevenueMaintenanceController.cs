using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Internal.Revenue.BackfillParcelVoucherReversals;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/revenue/backfills")]
public sealed class InternalRevenueMaintenanceController : ControllerBase
{
    private readonly ISender _sender;

    public InternalRevenueMaintenanceController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("parcel-voucher-reversals")]
    [ProducesResponseType(typeof(BackfillParcelVoucherReversalsResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BackfillParcelVoucherReversalsResult>> BackfillParcelVoucherReversalsAsync(
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
        => Ok(await _sender.Send(
            new BackfillParcelVoucherReversalsCommand(dryRun),
            cancellationToken).ConfigureAwait(false));
}
