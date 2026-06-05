namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record SetInitialPasswordRequest(
    string? Token,
    string Password);
