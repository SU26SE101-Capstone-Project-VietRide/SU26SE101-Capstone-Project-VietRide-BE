namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/users/me/complete-profile request body.</summary>
public sealed record CompleteProfileRequest(string Phone);
