namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
