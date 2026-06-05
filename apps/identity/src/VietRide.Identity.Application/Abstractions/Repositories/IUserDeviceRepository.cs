using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IUserDeviceRepository : IRepository<UserDevice, Guid>
{
    Task<UserDevice?> FindByUserAndFcmTokenAsync(
        Guid userId,
        string fcmToken,
        CancellationToken ct = default);

    Task<UserDevice?> FindByFcmTokenAsync(string fcmToken, CancellationToken ct = default);

    Task<IReadOnlyList<UserDevice>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
}
