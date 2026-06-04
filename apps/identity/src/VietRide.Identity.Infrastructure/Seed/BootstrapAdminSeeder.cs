using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Seed;

public sealed class BootstrapAdminSeeder
{
    private const int BCryptWorkFactor = 12;
    private const string DefaultDisplayName = "System Administrator";

    private readonly IdentityDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BootstrapAdminSeeder> _logger;

    public BootstrapAdminSeeder(
        IdentityDbContext db,
        IConfiguration configuration,
        ILogger<BootstrapAdminSeeder> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var hasSystemAdmin = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Role == UserRole.SYSTEM_ADMIN, ct);

        if (hasSystemAdmin)
        {
            _logger.LogInformation("System admin bootstrap skipped because a SYSTEM_ADMIN user already exists.");
            return;
        }

        var email = RequiredBootstrapValue("SYSTEM_ADMIN_BOOTSTRAP_EMAIL");
        var password = RequiredBootstrapValue("SYSTEM_ADMIN_BOOTSTRAP_PASSWORD");
        var displayName = OptionalBootstrapValue("SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME") ?? DefaultDisplayName;
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, BCryptWorkFactor);

        var insertedRows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_identity.users (email, password_hash, display_name, role, status)
SELECT {email}, {passwordHash}, {displayName}, 'SYSTEM_ADMIN'::user_role, 'ACTIVE'::user_status
WHERE NOT EXISTS (
    SELECT 1
    FROM vietride_identity.users
    WHERE role = 'SYSTEM_ADMIN'::user_role
);", ct);

        if (insertedRows == 0)
        {
            _logger.LogInformation("System admin bootstrap skipped because a SYSTEM_ADMIN user already exists.");
            return;
        }

        _logger.LogInformation("Bootstrapped initial SYSTEM_ADMIN user.");
    }

    private string RequiredBootstrapValue(string key)
    {
        var value = OptionalBootstrapValue(key);
        if (value is null)
            throw new InvalidOperationException($"{key} must be configured before Identity starts when no SYSTEM_ADMIN exists.");

        return value;
    }

    private string? OptionalBootstrapValue(string key)
    {
        var value = _configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
