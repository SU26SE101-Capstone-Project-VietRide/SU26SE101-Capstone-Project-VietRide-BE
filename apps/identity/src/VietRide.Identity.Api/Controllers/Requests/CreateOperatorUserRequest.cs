namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record CreateOperatorUserRequest(
    string Email,
    string Phone,
    string DisplayName,
    string Role);
