using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Seed;

public sealed class BootstrapAdminSeeder
{
    private const int BCryptWorkFactor = 12;
    private const string DefaultDisplayName = "System Administrator";

    private readonly ISystemAdminBootstrapStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BootstrapAdminSeeder> _logger;

    public BootstrapAdminSeeder(
        ISystemAdminBootstrapStore store,
        IConfiguration configuration,
        ILogger<BootstrapAdminSeeder> logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var hasSystemAdmin = await _store.HasSystemAdminAsync(ct);

        if (hasSystemAdmin)
        {
            _logger.LogInformation("System admin bootstrap skipped because a SYSTEM_ADMIN user already exists.");
            return;
        }

        var email = RequiredBootstrapValue("SYSTEM_ADMIN_BOOTSTRAP_EMAIL");
        var password = RequiredBootstrapValue("SYSTEM_ADMIN_BOOTSTRAP_PASSWORD");
        var displayName = OptionalBootstrapValue("SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME") ?? DefaultDisplayName;
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, BCryptWorkFactor);

        var inserted = await _store.InsertIfMissingAsync(
            new SystemAdminBootstrapUser(
                email,
                passwordHash,
                displayName,
                UserRole.SYSTEM_ADMIN,
                UserStatus.ACTIVE),
            ct);

        if (!inserted)
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
