using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Auth.ChangePassword;
using VietRide.Identity.Application.Features.Auth.ForgotPassword;
using VietRide.Identity.Application.Features.Auth.GoogleLogin;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Application.Features.Auth.Logout;
using VietRide.Identity.Application.Features.Auth.Refresh;
using VietRide.Identity.Application.Features.Auth.Register;
using VietRide.Identity.Application.Features.Auth.ResendVerificationEmail;
using VietRide.Identity.Application.Features.Auth.ResetPassword;
using VietRide.Identity.Application.Features.Auth.SetInitialPassword;
using VietRide.Identity.Application.Features.Auth.VerifyEmail;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// Authentication endpoints: register, verify-email, login, Google login, refresh, logout.
/// Paths per VietRide_API_Contract_v1.md Identity section (lines 18–116).
/// All success responses are wrapped in <see cref="ApiResponse{T}"/> by <c>ApiResponseResultFilter</c> (ADR 0004).
/// All error responses are wrapped in <see cref="ApiResponse"/> by <c>ApiResponseExceptionFilter</c> (ADR 0004).
/// </summary>
[ApiController]
[Route("v1/auth")]
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
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
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
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<VerifyEmailResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new VerifyEmailCommand(request.Email, request.Code, request.Purpose),
            ct);

        return Ok(result);
    }

    /// <summary>Resend an email verification OTP for an account pending email verification.</summary>
    /// <remarks>
    /// Already verified email → 409 AUTH_EMAIL_ALREADY_VERIFIED.
    /// Unknown email or invalid purpose → 400 AUTH_OTP_INVALID.
    /// OTP rate limit exceeded → 429 AUTH_OTP_RATE_LIMIT_EXCEEDED.
    /// </remarks>
    [HttpPost("resend-verification-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ResendVerificationEmailResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerificationEmail(
        [FromBody] ResendVerificationEmailRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new ResendVerificationEmailCommand(request.Email, request.Purpose),
            ct);

        return Ok(result);
    }

    /// <summary>Request a password-reset OTP email.</summary>
    /// <remarks>
    /// Always returns generic success for non-eligible emails to avoid account enumeration.
    /// OTP rate limit exceeded â†’ 429 AUTH_OTP_RATE_LIMIT_EXCEEDED.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ForgotPasswordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(new ForgotPasswordCommand(request.Email), ct);
        return Ok(result);
    }

    /// <summary>Reset a password using a password-reset OTP.</summary>
    /// <remarks>
    /// Wrong code â†’ 400 AUTH_OTP_INVALID.
    /// Expired code â†’ 400 AUTH_OTP_EXPIRED.
    /// Existing refresh tokens are revoked on success.
    /// </remarks>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ResetPasswordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new ResetPasswordCommand(request.Email, request.Code, request.NewPassword),
            ct);

        return Ok(result);
    }

    /// <summary>Changes the authenticated user's local password and revokes every active session.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<ChangePasswordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new ChangePasswordCommand(
                CurrentUserClaims.GetUserId(User),
                request.CurrentPassword,
                request.NewPassword,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString()),
            ct);

        return Ok(result);
    }

    /// <summary>Set the initial password using a one-time anonymous token.</summary>
    /// <remarks>
    /// Invalid, missing, or already-used token → 400 AUTH_INITIAL_PASSWORD_TOKEN_INVALID.
    /// Expired token → 400 AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED.
    /// Blank or weak password → 422 VALIDATION_ERROR.
    /// </remarks>
    [HttpPost("set-initial-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<SetInitialPasswordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetInitialPassword(
        [FromBody] SetInitialPasswordRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(new SetInitialPasswordCommand(request.Token, request.Password), ct);
        return Ok(result);
    }

    /// <summary>Authenticate with email and password. Returns access + refresh tokens.</summary>
    /// <remarks>
    /// Bad credentials → 401 AUTH_INVALID_CREDENTIALS.
    /// Non-passenger unverified email → 403 AUTH_EMAIL_NOT_VERIFIED.
    /// Locked account → 403 AUTH_ACCOUNT_LOCKED.
    /// </remarks>
    [HttpPost("login")]
    [SkipIdempotency("Login returns credentials and is protected by the native authentication lockout policy.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenBundleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new LoginCommand(
                request.Email,
                request.Password,
                ClientKindClassifier.Classify(Request.Headers.UserAgent.ToString())),
            ct);
        return Ok(result);
    }

    /// <summary>Authenticate with a Google ID token. Returns access + refresh tokens.</summary>
    /// <remarks>
    /// Invalid, expired, or wrong-audience Google ID token → 401 AUTH_GOOGLE_TOKEN_INVALID.
    /// </remarks>
    [HttpPost("google")]
    [SkipIdempotency("Google login returns credentials and is protected by provider-token validation.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenBundleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Google(
        [FromBody] GoogleLoginRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(
            new GoogleLoginCommand(
                request.IdToken,
                ClientKindClassifier.Classify(Request.Headers.UserAgent.ToString())),
            ct);
        return Ok(result);
    }

    /// <summary>Rotate a refresh token. Returns a new access + refresh token pair.</summary>
    /// <remarks>
    /// Invalid or expired token → 401 AUTH_TOKEN_INVALID.
    /// Reuse of a revoked token revokes the whole family → 401 AUTH_TOKEN_INVALID.
    /// </remarks>
    [HttpPost("refresh")]
    [SkipIdempotency("Refresh rotates credentials and is protected by refresh-token family replay detection.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenBundleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        var result = await _sender.Send(new RefreshCommand(request.RefreshToken), ct);
        return Ok(result);
    }

    /// <summary>Revoke the provided refresh token (logout). Auth required.</summary>
    /// <remarks>204 No Content — empty body (not wrapped in envelope per ADR 0004 Rule 5).</remarks>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken ct)
    {
        await _sender.Send(new LogoutCommand(request.RefreshToken), ct);
        return NoContent();
    }
}
