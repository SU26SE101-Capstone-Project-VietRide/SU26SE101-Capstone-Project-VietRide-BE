using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class SubscriptionUsageWarningMarkerRepository
    : ISubscriptionUsageWarningMarkerRepository
{
    private readonly IdentityDbContext _db;

    public SubscriptionUsageWarningMarkerRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public Task<SubscriptionUsageWarningMarker?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _db.SubscriptionUsageWarningMarkers.SingleOrDefaultAsync(
            marker => marker.Id == id,
            cancellationToken);

    public async Task<SubscriptionUsageWarningMarker> AddAsync(
        SubscriptionUsageWarningMarker entity,
        CancellationToken cancellationToken = default)
    {
        await _db.SubscriptionUsageWarningMarkers.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(SubscriptionUsageWarningMarker entity) => _db.Update(entity);
    public void Remove(SubscriptionUsageWarningMarker entity) => _db.Remove(entity);
    public IQueryable<SubscriptionUsageWarningMarker> Query() => _db.SubscriptionUsageWarningMarkers;
    public IQueryable<SubscriptionUsageWarningMarker> QueryNoTracking() => Query().AsNoTracking();

    public Task<bool> ExistsAsync(
        Guid subscriptionId,
        SubscriptionUsageResource resource,
        string periodKey,
        CancellationToken cancellationToken)
        => _db.SubscriptionUsageWarningMarkers.AnyAsync(
            marker => marker.SubscriptionId == subscriptionId
                && marker.Resource == resource
                && marker.PeriodKey == periodKey,
            cancellationToken);
}
