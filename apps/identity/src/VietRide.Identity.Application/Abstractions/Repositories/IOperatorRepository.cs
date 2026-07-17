using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;

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

    Task<PagedResult<Operator>> ListAsync(
        QueryOptions options,
        OperatorRegistrationStatus? status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Operator>> ListSummariesByIdsAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator summary listing is not implemented by this repository.");
}
