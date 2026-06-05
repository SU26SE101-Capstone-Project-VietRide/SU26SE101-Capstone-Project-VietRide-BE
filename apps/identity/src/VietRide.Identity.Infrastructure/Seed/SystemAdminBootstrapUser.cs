using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Seed;

public sealed record SystemAdminBootstrapUser(
    string Email,
    string PasswordHash,
    string DisplayName,
    UserRole Role,
    UserStatus Status);
