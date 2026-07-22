using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Payments.DispatchVnPayIpn;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("v1/payments")]
public sealed class VnPaySubscriptionIpnController : ControllerBase
{
    private readonly ISender _sender;

    public VnPaySubscriptionIpnController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("subscription-vnpay-ipn")]
    [SkipIdempotency("VNPay IPN is authenticated and deduplicated by the provider transaction reference.")]
    public async Task<IActionResult> ConfirmAsync(CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Request.Query)
            parameters[pair.Key] = pair.Value.ToString();
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            foreach (var pair in form)
                parameters[pair.Key] = pair.Value.ToString();
        }

        var result = await _sender.Send(new DispatchVnPayIpnCommand(parameters), cancellationToken);
        return new JsonResult(result) { StatusCode = StatusCodes.Status200OK };
    }
}
