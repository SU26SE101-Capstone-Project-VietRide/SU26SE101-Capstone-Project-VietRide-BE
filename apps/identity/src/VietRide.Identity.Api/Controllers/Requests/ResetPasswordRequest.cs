namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record ResetPasswordRequest(
    string Email,
    string Code,
    string NewPassword);
