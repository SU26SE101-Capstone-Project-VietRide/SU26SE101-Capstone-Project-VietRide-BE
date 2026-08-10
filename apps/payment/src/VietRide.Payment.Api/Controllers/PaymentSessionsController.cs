using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Features.Payments.GetPaymentSessionStatus;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Route("v1/payments/sessions")]
[Authorize(Roles = "PASSENGER")]
public sealed class PaymentSessionsController : ControllerBase
{
    private readonly ISender _sender;

    public PaymentSessionsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Get the normalized state of an authenticated passenger's Mobile SDK payment session.</summary>
    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentSessionStatusResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid sessionId, CancellationToken ct)
    {
        var result = await _sender.Send(new GetPaymentSessionStatusQuery(sessionId, GetUserId()), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var subject = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");

        return userId;
    }
}
