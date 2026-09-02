using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Application.Features.OperatorReports;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class OperatorLedgerEntryRepository : IOperatorLedgerEntryRepository
{
    private readonly PaymentDbContext _db;
    public OperatorLedgerEntryRepository(PaymentDbContext db) => _db = db;
    public Task<OperatorLedgerEntry?> GetByIdAsync(Guid id, CancellationToken ct) => _db.OperatorLedgerEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<OperatorLedgerEntry> AddAsync(OperatorLedgerEntry entity, CancellationToken ct) { await _db.OperatorLedgerEntries.AddAsync(entity, ct); return entity; }
    public void Update(OperatorLedgerEntry entity) => throw new NotSupportedException("Operator ledger is immutable.");
    public void Remove(OperatorLedgerEntry entity) => throw new NotSupportedException("Operator ledger is immutable.");
    public IQueryable<OperatorLedgerEntry> Query() => _db.OperatorLedgerEntries;
    public IQueryable<OperatorLedgerEntry> QueryNoTracking() => _db.OperatorLedgerEntries.AsNoTracking();

    public async Task<IReadOnlyList<PlatformLedgerReportItem>> GetPlatformLedgerMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT operator_id,
                   COALESCE(SUM(amount) FILTER (
                       WHERE {CanonicalRevenueSql.BookingPredicate}
                   ), 0)::numeric AS booking_revenue_vnd,
                   COALESCE(SUM(amount) FILTER (
                       WHERE {CanonicalRevenueSql.ParcelPredicate}
                   ), 0)::numeric AS parcel_revenue_vnd
            FROM vietride_payment.operator_ledger_entries
            WHERE created_at >= @from_utc
              AND created_at < @to_utc
              AND {CanonicalRevenueSql.RecognizedPredicate}
            GROUP BY operator_id
            ORDER BY operator_id;
            """;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "from_utc", fromUtc.ToUniversalTime());
        AddParameter(command, "to_utc", toUtc.ToUniversalTime());

        var result = new List<PlatformLedgerReportItem>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                result.Add(new PlatformLedgerReportItem(
                    reader.GetGuid(0),
                    checked((long)reader.GetDecimal(1)),
                    checked((long)reader.GetDecimal(2))));
            }
        }
        catch (OverflowException exception)
        {
            throw new PlatformReportValueOverflowException(exception);
        }

        return result;
    }

    public async IAsyncEnumerable<OperatorLedgerReportRow> StreamOperatorReportRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool refundOnly,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        var predicate = refundOnly
            ? CanonicalRevenueSql.RefundPredicate
            : CanonicalRevenueSql.RecognizedPredicate;
        command.CommandText = $"""
            SELECT entry.id,
                   entry.entry_type::text,
                   entry.reference_type::text,
                   entry.adjustment_reason::text,
                   entry.reference_id,
                   entry.trip_id,
                   entry.amount,
                   entry.created_at,
                   entry.note,
                   entry.reference_code,
                   settlement.trip_code
            FROM vietride_payment.operator_ledger_entries AS entry
            LEFT JOIN vietride_payment.operator_trip_settlements AS settlement
              ON settlement.operator_id = entry.operator_id
             AND settlement.trip_id = entry.trip_id
            WHERE entry.operator_id = @operator_id
              AND entry.created_at >= @from_utc
              AND entry.created_at < @to_utc
              AND {predicate}
            ORDER BY entry.created_at, entry.id;
            """;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "operator_id", operatorId);
        AddParameter(command, "from_utc", fromUtc.ToUniversalTime());
        AddParameter(command, "to_utc", toUtc.ToUniversalTime());

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return new OperatorLedgerReportRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetInt64(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10));
        }
    }

    public async Task<IReadOnlyList<Guid>> ListOperatorReportTripIdsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
        => await _db.OperatorLedgerEntries
            .AsNoTracking()
            .Where(item => item.OperatorId == operatorId
                && item.TripId.HasValue
                && item.CreatedAt >= fromUtc
                && item.CreatedAt < toUtc
                && !_db.OperatorTripSettlements.Any(settlement =>
                    settlement.OperatorId == operatorId
                    && settlement.TripId == item.TripId.Value
                    && settlement.TripCode != null))
            .Select(item => item.TripId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArrayAsync(cancellationToken);

    public async Task<long> SumTripNetAmountAsync(
        Guid operatorId,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var projection = await GetTripFinancialProjectionsAsync(
            operatorId,
            [tripId],
            cancellationToken).ConfigureAwait(false);
        return projection.SingleOrDefault()?.NetEntitlementAmount ?? 0;
    }

    public async Task<IReadOnlyList<TripFinancialProjection>> GetTripFinancialProjectionsAsync(
        Guid operatorId,
        IReadOnlyCollection<Guid>? tripIds,
        CancellationToken cancellationToken)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id must be non-empty.", nameof(operatorId));

        var normalizedTripIds = tripIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (tripIds is not null && normalizedTripIds!.Length == 0)
            return [];

        var query = CanonicalTripFinancialProjectionQuery.ForOperator(_db, operatorId);
        if (normalizedTripIds is not null)
            query = query.Where(item => normalizedTripIds.Contains(item.TripId));
        return await query.OrderBy(item => item.TripId).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> HasSourceEntryAsync(
        Guid sourceEventId,
        Guid referenceId,
        CancellationToken cancellationToken)
        => _db.OperatorLedgerEntries.AnyAsync(
            entry => entry.SourceEventId == sourceEventId
                && entry.ReferenceId == referenceId,
            cancellationToken);

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
