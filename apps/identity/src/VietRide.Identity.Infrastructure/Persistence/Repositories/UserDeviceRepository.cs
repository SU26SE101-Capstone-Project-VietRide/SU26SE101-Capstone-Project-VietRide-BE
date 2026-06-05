using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class UserDeviceRepository : IUserDeviceRepository
{
    private readonly IdentityDbContext _db;

    public UserDeviceRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<UserDevice?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.UserDevices.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<UserDevice?> FindByUserAndFcmTokenAsync(
        Guid userId,
        string fcmToken,
        CancellationToken ct = default)
        => await QueryByUserAndFcmToken(_db.UserDevices, userId, fcmToken)
            .FirstOrDefaultAsync(ct);

    public async Task<UserDevice?> FindByFcmTokenAsync(string fcmToken, CancellationToken ct = default)
        => await QueryActiveByFcmToken(_db.UserDevices, fcmToken)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<UserDevice>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await QueryActiveByUserId(_db.UserDevices, userId)
            .OrderByDescending(d => d.LastActiveAt)
            .ToListAsync(ct);

    internal static IQueryable<UserDevice> QueryByUserAndFcmToken(
        IQueryable<UserDevice> devices,
        Guid userId,
        string fcmToken)
        => devices.Where(d => d.UserId == userId && d.FcmToken == fcmToken);

    internal static IQueryable<UserDevice> QueryActiveByFcmToken(
        IQueryable<UserDevice> devices,
        string fcmToken)
        => devices.Where(d => d.FcmToken == fcmToken && d.IsActive);

    internal static IQueryable<UserDevice> QueryActiveByUserId(IQueryable<UserDevice> devices, Guid userId)
        => devices.Where(d => d.UserId == userId && d.IsActive);

    public async Task<UserDevice> AddAsync(UserDevice entity, CancellationToken ct)
    {
        await _db.UserDevices.AddAsync(entity, ct);
        return entity;
    }

    public void Update(UserDevice entity)
        => _db.UserDevices.Update(entity);

    public void Remove(UserDevice entity)
        => _db.UserDevices.Remove(entity);

    public IQueryable<UserDevice> Query()
        => _db.UserDevices;

    public IQueryable<UserDevice> QueryNoTracking()
        => _db.UserDevices.AsNoTracking();
}
