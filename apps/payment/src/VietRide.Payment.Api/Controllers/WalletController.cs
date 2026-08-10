using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Api.Controllers.Requests;
using VietRide.Payment.Application.Features.TopUps.CreateTopUp;
using VietRide.Payment.Application.Features.Wallets.GetWallet;
using VietRide.Payment.Application.Features.Wallets.GetWalletTransactions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

/// <summary>
/// Passenger wallet endpoints. Successful responses are wrapped in the ADR 0004 ApiResponse envelope by the shared result filter.
/// </summary>
[ApiController]
[Route("v1/wallet")]
[Authorize]
public sealed class WalletController : ControllerBase
{
    private const string PassengerRole = "PASSENGER";
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";

    private readonly ISender _sender;

    public WalletController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Get the authenticated user's passenger wallet.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<GetWalletResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetWalletResult>> GetWallet(CancellationToken ct)
    {
        var result = await _sender.Send(new GetWalletQuery(GetUserId()), ct);
        return Ok(result);
    }

    /// <summary>Get the authenticated user's passenger wallet ledger newest first.</summary>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<GetWalletTransactionResult>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<GetWalletTransactionResult>>> GetWalletTransactions(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new GetWalletTransactionsQuery(
            GetUserId(),
            from,
            to,
            type,
            page,
            pageSize);

        var result = await _sender.Send(query, ct);
        return Ok(result);
    }

    /// <summary>Create a VNPay redirect URL for a passenger wallet top-up.</summary>
    /// <remarks>
    /// Auth: PASSENGER (RS256 user token via JWKS).
    /// Idempotency-Key header required — same key + same body returns the same 201 response.
    /// amount &lt; 10,000 VND → 422 WALLET_TOP_UP_AMOUNT_TOO_LOW.
    /// </remarks>
    [HttpPost("top-up")]
    [Authorize(Roles = PassengerRole)]
    [ProducesResponseType(typeof(ApiResponse<CreateTopUpResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status426UpgradeRequired)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateTopUp(
        [FromBody] CreateTopUpRequest request,
        CancellationToken ct)
    {
        RequireIdempotencyKey();

        var userId = GetUserId();
        var command = new CreateTopUpCommand(
            userId,
            request.Amount,
            request.Method,
            ResolveClientIpAddress(),
            request.PaymentReturnMode);

        var result = await _sender.Send(command, ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    private void RequireIdempotencyKey()
    {
        if (Request.Headers.TryGetValue(IdempotencyKeyHeaderName, out var values)
            && values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            return;
        }

        throw new ValidationException(
            "Idempotency-Key header is required.",
            [new ValidationError(IdempotencyKeyHeaderName, "Idempotency-Key header is required.")]);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");

        return userId;
    }

    private string ResolveClientIpAddress()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor)
            && !string.IsNullOrWhiteSpace(forwardedFor.ToString()))
        {
            return forwardedFor.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }
}
