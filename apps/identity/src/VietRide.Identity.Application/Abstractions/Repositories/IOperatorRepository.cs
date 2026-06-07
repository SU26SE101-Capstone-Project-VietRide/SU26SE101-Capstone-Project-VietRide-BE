using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IOperatorRepository : IRepository<Operator, Guid>
{
    Task<Operator?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

    async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => await GetByIdNoTrackingAsync(id, cancellationToken) is not null;

    Task<Operator?> GetByBusinessRegistrationNumberAsync(
        string businessRegistrationNumber,
        CancellationToken cancellationToken = default);

    Task<Operator?> GetByTaxCodeAsync(
        string taxCode,
        CancellationToken cancellationToken = default);
}
