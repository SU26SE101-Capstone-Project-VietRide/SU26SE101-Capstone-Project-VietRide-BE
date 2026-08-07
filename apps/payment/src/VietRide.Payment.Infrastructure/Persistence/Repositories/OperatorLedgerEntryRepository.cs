using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
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
            SELECT id,
                   entry_type::text,
                   reference_type::text,
                   reference_id,
                   trip_id,
                   amount,
                   created_at,
                   note
            FROM vietride_payment.operator_ledger_entries
            WHERE operator_id = @operator_id
              AND created_at >= @from_utc
              AND created_at < @to_utc
              AND {predicate}
            ORDER BY created_at, id;
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
                reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetInt64(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetString(7));
        }
    }
    public Task<long> SumTripNetAmountAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken)
        => _db.OperatorLedgerEntries.Where(x => x.OperatorId == operatorId && x.TripId == tripId).SumAsync(x => x.Amount, cancellationToken);

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
