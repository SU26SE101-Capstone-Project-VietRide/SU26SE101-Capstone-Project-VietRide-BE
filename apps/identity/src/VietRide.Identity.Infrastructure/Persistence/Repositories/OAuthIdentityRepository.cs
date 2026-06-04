using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class OAuthIdentityRepository : IOAuthIdentityRepository
{
    private readonly IdentityDbContext _dbContext;

    public OAuthIdentityRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OAuthIdentity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<OAuthIdentity>().FindAsync([id], cancellationToken);
    }

    public async Task<OAuthIdentity> AddAsync(
        OAuthIdentity entity,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<OAuthIdentity>().AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(OAuthIdentity entity)
    {
        _dbContext.Set<OAuthIdentity>().Update(entity);
    }

    public void Remove(OAuthIdentity entity)
    {
        _dbContext.Set<OAuthIdentity>().Remove(entity);
    }

    public IQueryable<OAuthIdentity> Query()
    {
        return _dbContext.Set<OAuthIdentity>();
    }

    public IQueryable<OAuthIdentity> QueryNoTracking()
    {
        return _dbContext.Set<OAuthIdentity>().AsNoTracking();
    }

    public async Task<User?> GetUserByProviderSubjectAsync(
        OAuthProvider provider,
        string providerSubject,
        CancellationToken cancellationToken = default)
    {
        var link = await _dbContext.Set<OAuthIdentity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                identity => identity.Provider == provider
                    && identity.ProviderSubject == providerSubject,
                cancellationToken);

        if (link is null)
            return null;

        return await _dbContext.Set<User>()
            .FirstOrDefaultAsync(user => user.Id == link.UserId, cancellationToken);
    }
}
