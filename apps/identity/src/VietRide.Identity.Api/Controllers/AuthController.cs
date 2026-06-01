using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Application.Features.Auth.Logout;
using VietRide.Identity.Application.Features.Auth.Refresh;
using VietRide.Identity.Application.Features.Auth.Register;
using VietRide.Identity.Application.Features.Auth.VerifyEmail;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// Authentication endpoints: register, verify-email, login, refresh, logout.
/// Paths per VietRide_API_Contract_v1.md Identity section (lines 18–116).
/// </summary>
[ApiController]
[Route("v1/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Register a new PASSENGER account.</summary>
    /// <remarks>
    /// Sends a 6-digit OTP to the provided email address.
    /// Duplicate email → 409 AUTH_EMAIL_ALREADY_REGISTERED.
    /// Duplicate phone → 409 AUTH_PHONE_ALREADY_REGISTERED.
    /// Invalid phone format → 400 AUTH_PHONE_INVALID_FORMAT.
    /// OTP rate limit exceeded → 429 AUTH_OTP_RATE_LIMIT_EXCEEDED.
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new RegisterCommand(request.Email, request.Password, request.DisplayName, request.Phone),
            ct);

        // SF1: return 201 via StatusCode, not CreatedAtAction (no GET-by-id route exists).
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Verify an email address with a 6-digit OTP code.</summary>
    /// <remarks>
    /// Wrong code → 400 AUTH_OTP_INVALID (increments failed_attempts; invalidated after 5 failures).
    /// Expired code → 400 AUTH_OTP_EXPIRED.
    /// </remarks>
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(VerifyEmailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new VerifyEmailCommand(request.Email, request.Code, request.Purpose),
            ct);

        return Ok(result);
    }

    /// <summary>Authenticate with email and password. Returns access + refresh tokens.</summary>
    /// <remarks>
    /// Bad credentials → 401 AUTH_INVALID_CREDENTIALS.
    /// Unverified email → 403 AUTH_EMAIL_NOT_VERIFIED.
    /// Locked account → 403 AUTH_ACCOUNT_LOCKED.
    /// </remarks>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenBundleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(new LoginCommand(request.Email, request.Password), ct);
        return Ok(result);
    }

    /// <summary>Rotate a refresh token. Returns a new access + refresh token pair.</summary>
    /// <remarks>
    /// Invalid or expired token → 401 AUTH_TOKEN_INVALID.
    /// Reuse of a revoked token revokes the whole family → 401 AUTH_TOKEN_INVALID.
    /// </remarks>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenBundleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(new RefreshCommand(request.RefreshToken), ct);
        return Ok(result);
    }

    /// <summary>Revoke the provided refresh token (logout). Auth required.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken ct)
    {
        await _sender.Send(new LogoutCommand(request.RefreshToken), ct);
        return NoContent();
    }
}
