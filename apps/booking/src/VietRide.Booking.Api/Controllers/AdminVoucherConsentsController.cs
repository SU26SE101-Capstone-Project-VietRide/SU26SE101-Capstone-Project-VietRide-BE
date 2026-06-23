using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

/// <summary>
/// Admin voucher-consent view endpoints (SYSTEM_ADMIN only):
/// <list type="bullet">
///   <item>GET /v1/admin/vouchers/{voucherId}/consents — list all consent records for a given voucher.</item>
/// </list>
/// All responses wrapped in <see cref="ApiResponse{T}"/> by ApiResponseResultFilter (ADR 0004).
/// </summary>
[ApiController]
[Route("v1/admin/vouchers/{voucherId:guid}/consents")]
[Authorize(Roles = SystemAdminRole)]
public sealed class AdminVoucherConsentsController : ControllerBase
{
    private const string SystemAdminRole = "SYSTEM_ADMIN";

    private readonly ISender _sender;

    public AdminVoucherConsentsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Returns all consent records for a given voucher (admin governance view).</summary>
    /// <remarks>
    /// Auth: SYSTEM_ADMIN (RS256 user token via JWKS).
    /// Read-only — no Idempotency-Key required.
    /// Returns every (operator, voucher) consent row regardless of status.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminVoucherConsentsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListConsents(
        [FromRoute] Guid voucherId,
        CancellationToken ct)
    {
        var query = new ListAdminVoucherConsentsQuery(VoucherId: voucherId);
        var result = await _sender.Send(query, ct);
        return Ok(result);
    }
}
