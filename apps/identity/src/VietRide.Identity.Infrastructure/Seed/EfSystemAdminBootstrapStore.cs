using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Seed;

public sealed class EfSystemAdminBootstrapStore : ISystemAdminBootstrapStore
{
    private readonly IdentityDbContext _db;

    public EfSystemAdminBootstrapStore(IdentityDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasSystemAdminAsync(CancellationToken ct)
        => _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Role == UserRole.SYSTEM_ADMIN, ct);

    public async Task<bool> InsertIfMissingAsync(SystemAdminBootstrapUser user, CancellationToken ct)
    {
        var role = user.Role.ToString();
        var status = user.Status.ToString();
        var systemAdminRole = UserRole.SYSTEM_ADMIN.ToString();

        var insertedRows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_identity.users (email, password_hash, display_name, role, status)
SELECT {user.Email}, {user.PasswordHash}, {user.DisplayName}, {role}::user_role, {status}::user_status
WHERE NOT EXISTS (
    SELECT 1
    FROM vietride_identity.users
    WHERE role = {systemAdminRole}::user_role
);", ct);

        return insertedRows > 0;
    }
}
