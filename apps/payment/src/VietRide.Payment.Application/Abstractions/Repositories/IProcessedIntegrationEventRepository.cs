using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IProcessedIntegrationEventRepository : IRepository<ProcessedIntegrationEvent, Guid>
{
    Task<bool> ExistsAsync(string consumer, Guid eventId, CancellationToken cancellationToken);
}
