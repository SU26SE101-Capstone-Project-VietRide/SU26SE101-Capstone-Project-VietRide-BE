using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _db;

    public UserRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User?> GetByEmailAsync(string emailLower, CancellationToken ct = default)
        => await _db.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower, ct);

    public async Task<User?> GetByPhoneAsync(string e164Phone, CancellationToken ct = default)
        => await _db.Users
            .FirstOrDefaultAsync(u => u.Phone != null && u.Phone.Value.Value == e164Phone, ct);

    public async Task<User> AddAsync(User entity, CancellationToken ct)
    {
        await _db.Users.AddAsync(entity, ct);
        return entity;
    }

    public void Update(User entity)
        => _db.Users.Update(entity);

    public void Remove(User entity)
        => _db.Users.Remove(entity);

    public IQueryable<User> Query()
        => _db.Users;

    public IQueryable<User> QueryNoTracking()
        => _db.Users.AsNoTracking();
}
