namespace VietRide.Identity.Infrastructure.Seed;

public interface ISystemAdminBootstrapStore
{
    Task<bool> HasSystemAdminAsync(CancellationToken ct);

    Task<bool> InsertIfMissingAsync(SystemAdminBootstrapUser user, CancellationToken ct);
}
