using VietRide.Identity.Application.Features.Admin.GetOperatorSummary;
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

    Task<PagedResult<Operator>> ListFilteredAsync(
        QueryOptions options,
        OperatorRegistrationStatus? status,
        bool? isActive = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtcExclusive = null,
        string dateField = "createdAt",
        CancellationToken cancellationToken = default)
        => ListAsync(options, status, cancellationToken);

    Task<IReadOnlyList<Operator>> ListForExportAsync(
        QueryOptions options,
        OperatorRegistrationStatus? status,
        bool? isActive,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        string dateField,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator export is not implemented by this repository.");

    Task<AdminOperatorSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AdminOperatorSummaryDto(0, 0, 0, 0, 0, 0));

    Task<IReadOnlyList<Operator>> ListSummariesByIdsAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator summary listing is not implemented by this repository.");
}
