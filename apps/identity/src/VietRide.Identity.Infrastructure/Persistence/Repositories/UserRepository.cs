using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
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
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Phone == phone, ct);
    }

    public async Task<User?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        var tracked = _db.Users.Local.FirstOrDefault(user => user.Id == id);
        if (tracked is not null)
            _db.Entry(tracked).State = EntityState.Detached;

        return await _db.Users
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.users WHERE id = {id} AND deleted_at IS NULL FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(ct);
    }

    public async Task<PagedResult<User>> ListAdminUsersAsync(
        QueryOptions options,
        UserRole? role,
        UserStatus? status,
        Guid? operatorId,
        CancellationToken ct = default)
    {
        IQueryable<User> query;
        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var searchPattern = $"%{options.Search.Trim()}%";
            query = _db.Users
                .FromSqlInterpolated($"SELECT * FROM vietride_identity.users WHERE email ILIKE {searchPattern} OR display_name ILIKE {searchPattern} OR phone ILIKE {searchPattern}")
                .AsNoTracking();
        }
        else
        {
            query = _db.Users.AsNoTracking();
        }

        if (options.IncludeDeleted)
            query = query.IgnoreQueryFilters();

        if (role.HasValue)
            query = query.Where(user => user.Role == role.Value);

        if (status.HasValue)
            query = query.Where(user => user.Status == status.Value);

        if (operatorId.HasValue)
            query = query.Where(user => user.OperatorId == operatorId.Value);

        var totalItems = await query.LongCountAsync(ct);
        var items = await ApplySort(query, options.SortBy, options.SortDir)
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToListAsync(ct);

        return PagedResult<User>.Create(items, options.Page, options.PageSize, totalItems);
    }

    public async Task<PagedResult<User>> ListOperatorUsersAsync(
        QueryOptions options,
        Guid? operatorId,
        UserRole? role,
        UserStatus? status,
        CancellationToken ct = default)
    {
        var allowedRoles = new[] { UserRole.DRIVER, UserRole.ASSISTANT, UserRole.OPERATOR_STAFF };
        var query = _db.Users
            .AsNoTracking()
            .Where(u => allowedRoles.Contains(u.Role) && u.OperatorId.HasValue);

        if (operatorId.HasValue)
            query = query.Where(u => u.OperatorId == operatorId.Value);

        if (role.HasValue)
            query = query.Where(u => u.Role == role.Value);

        if (status.HasValue)
            query = query.Where(u => u.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var normalizedSearch = options.Search.Trim();
            var searchPattern = $"%{normalizedSearch}%";
            var phoneSearch = TryNormalizePhone(normalizedSearch);
            query = phoneSearch.HasValue
                ? query.Where(u =>
                    EF.Functions.ILike(u.Email, searchPattern)
                    || EF.Functions.ILike(u.DisplayName, searchPattern)
                    || u.Phone == phoneSearch.Value)
                : query.Where(u =>
                    EF.Functions.ILike(u.Email, searchPattern)
                    || EF.Functions.ILike(u.DisplayName, searchPattern));
        }

        var totalItems = await query.LongCountAsync(ct);
        var items = await ApplySort(query, options.SortBy, options.SortDir)
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToListAsync(ct);

        return PagedResult<User>.Create(items, options.Page, options.PageSize, totalItems);
    }

    public async Task<IReadOnlyList<Guid>> ListActiveOperatorAdminIdsAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Users
            .AsNoTracking()
            .Where(u =>
                u.OperatorId == operatorId
                && u.Role == UserRole.OPERATOR_ADMIN
                && u.Status == UserStatus.ACTIVE)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListOperatorAdminIdsAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Users
            .AsNoTracking()
            .Where(u => u.OperatorId == operatorId && u.Role == UserRole.OPERATOR_ADMIN)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListOperatorScopedUserIdsAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Users
            .AsNoTracking()
            .Where(u => u.OperatorId == operatorId)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .ToListAsync(ct);

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

    private static PhoneNumber? TryNormalizePhone(string value)
    {
        try
        {
            return PhoneNumber.Normalize(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IOrderedQueryable<User> ApplySort(
        IQueryable<User> query,
        string? sortBy,
        string sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var ordered = sortBy?.Trim().ToLowerInvariant() switch
        {
            "email" => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "displayname" => descending ? query.OrderByDescending(u => u.DisplayName) : query.OrderBy(u => u.DisplayName),
            "role" => descending ? query.OrderByDescending(u => u.Role) : query.OrderBy(u => u.Role),
            "status" => descending ? query.OrderByDescending(u => u.Status) : query.OrderBy(u => u.Status),
            _ => descending ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt),
        };

        return descending ? ordered.ThenByDescending(u => u.Id) : ordered.ThenBy(u => u.Id);
    }
}
