using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Application.Features.OperatorReports;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IOperatorLedgerEntryRepository : IRepository<OperatorLedgerEntry, Guid>
{
    Task<IReadOnlyList<PlatformLedgerReportItem>> GetPlatformLedgerMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
        => throw new NotSupportedException("Platform ledger report is not implemented by this repository.");
    IAsyncEnumerable<OperatorLedgerReportRow> StreamOperatorReportRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool refundOnly,
        CancellationToken ct = default)
        => throw new NotSupportedException("Operator ledger report is not implemented by this repository.");
    Task<long> SumTripNetAmountAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TripFinancialProjection>> GetTripFinancialProjectionsAsync(
        Guid operatorId,
        IReadOnlyCollection<Guid>? tripIds,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Trip financial projection is not implemented by this repository.");
    Task<bool> HasSourceEntryAsync(
        Guid sourceEventId,
        Guid referenceId,
        CancellationToken cancellationToken)
        => Task.FromResult(false);
}
