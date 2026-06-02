using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.ValueObjects;

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
    {
        // u.Phone is mapped via ValueConverter<PhoneNumber?, string?>; comparing the
        // CLR PhoneNumber? value against a constant allows EF Core to translate the
        // predicate into a SQL WHERE phone = @e164Phone via the converter's
        // ConvertToProviderExpression (constant on the RHS is evaluated at plan time).
        PhoneNumber? phone = PhoneNumber.Parse(e164Phone);
        return await _db.Users
            .FirstOrDefaultAsync(u => u.Phone == phone, ct);
    }

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
