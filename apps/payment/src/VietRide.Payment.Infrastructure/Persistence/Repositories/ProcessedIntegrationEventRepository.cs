using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class ProcessedIntegrationEventRepository : IProcessedIntegrationEventRepository
{
    private readonly PaymentDbContext _db;
    public ProcessedIntegrationEventRepository(PaymentDbContext db) => _db = db;
    public Task<ProcessedIntegrationEvent?> GetByIdAsync(Guid id, CancellationToken ct) => _db.ProcessedIntegrationEvents.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<ProcessedIntegrationEvent> AddAsync(ProcessedIntegrationEvent entity, CancellationToken ct) { await _db.ProcessedIntegrationEvents.AddAsync(entity, ct); return entity; }
    public void Update(ProcessedIntegrationEvent entity) => throw new NotSupportedException("Processed-event markers are immutable.");
    public void Remove(ProcessedIntegrationEvent entity) => throw new NotSupportedException("Processed-event markers are immutable.");
    public IQueryable<ProcessedIntegrationEvent> Query() => _db.ProcessedIntegrationEvents;
    public IQueryable<ProcessedIntegrationEvent> QueryNoTracking() => _db.ProcessedIntegrationEvents.AsNoTracking();
    public Task<bool> ExistsAsync(string consumer, Guid eventId, CancellationToken cancellationToken)
        => _db.ProcessedIntegrationEvents.AnyAsync(x => x.Consumer == consumer && x.EventId == eventId, cancellationToken);
}
