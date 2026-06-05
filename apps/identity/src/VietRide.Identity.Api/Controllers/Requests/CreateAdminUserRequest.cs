namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/admin/users request body.</summary>
public sealed record CreateAdminUserRequest(
    string Email,
    string DisplayName,
    string Role);
