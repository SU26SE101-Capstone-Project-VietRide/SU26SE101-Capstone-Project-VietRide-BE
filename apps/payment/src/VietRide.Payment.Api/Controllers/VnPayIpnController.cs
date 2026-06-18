using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.TopUps.ConfirmTopUp;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("v1/payments")]
public sealed class VnPayIpnController : ControllerBase
{
    private readonly ISender _sender;

    public VnPayIpnController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Public VNPay wallet top-up IPN callback. Returns VNPay machine-to-machine JSON, not ApiResponse.
    /// </summary>
    [HttpPost("vnpay-topup-ipn")]
    [ProducesResponseType(typeof(ConfirmTopUpResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConfirmTopUpResult), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmTopUp(CancellationToken ct)
    {
        var parameters = await ReadVnPayParametersAsync(ct);
        var result = await _sender.Send(new ConfirmTopUpCommand(parameters), ct);

        // VNPay expects this exact machine-to-machine JSON shape, so this endpoint intentionally bypasses ApiResponse.
        return new JsonResult(result) { StatusCode = result.StatusCode };
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
