using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Payments.DispatchVnPayIpn;
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
