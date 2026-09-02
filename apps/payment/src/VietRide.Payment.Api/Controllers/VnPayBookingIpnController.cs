using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Payments.DispatchVnPayIpn;
using VietRide.Payment.Application.Features.Payments.GetVnPayMobileSdkReturn;
using VietRide.Payment.Application.Features.Payments.GetVnPayReturnStatus;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("v1/payments")]
public sealed class VnPayBookingIpnController : ControllerBase
{
    private readonly ISender _sender;

    public VnPayBookingIpnController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Public VNPay booking payment IPN callback. Returns VNPay machine-to-machine JSON, not ApiResponse.
    /// </summary>
    [HttpGet("vnpay-ipn")]
    [HttpPost("vnpay-ipn")]
    [SkipIdempotency("VNPay IPN is authenticated and deduplicated by the provider transaction reference.")]
    [ProducesResponseType(typeof(DispatchVnPayIpnResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmPayment(CancellationToken ct)
    {
        var parameters = await ReadVnPayParametersAsync(ct);
        var result = await _sender.Send(new DispatchVnPayIpnCommand(parameters), ct);

        return new JsonResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Status used by the Manager Web return page. Signed VNPay query parameters authenticate
    /// the lookup. A signed subscription cancellation terminalizes its pending Payment as failed;
    /// all other return outcomes remain read-only and successful capture stays IPN-owned.
    /// </summary>
    [HttpGet("vnpay-return-status")]
    [ProducesResponseType(typeof(ApiResponse<VnPayReturnStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetReturnStatus(CancellationToken ct)
    {
        var parameters = await ReadVnPayParametersAsync(ct);
        var result = await _sender.Send(new GetVnPayReturnStatusQuery(parameters), ct);
        return Ok(result);
    }

    /// <summary>
    /// Technical return endpoint for the VNPay Mobile SDK. It verifies the signed provider
    /// parameters and redirects to the SDK's fixed success, cancel, or failure URI.
    /// This endpoint never changes payment state; VNPay IPN remains the mutation source of truth.
    /// </summary>
    [HttpGet("vnpay-mobile-sdk-return")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMobileSdkReturn(CancellationToken ct)
    {
        var parameters = await ReadVnPayParametersAsync(ct);
        var result = await _sender.Send(new GetVnPayMobileSdkReturnQuery(parameters), ct);
        return Redirect(result.RedirectUri);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadVnPayParametersAsync(CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in Request.Query)
        {
            values[pair.Key] = pair.Value.ToString();
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            foreach (var pair in form)
            {
                values[pair.Key] = pair.Value.ToString();
            }
        }

        return values;
    }
}
