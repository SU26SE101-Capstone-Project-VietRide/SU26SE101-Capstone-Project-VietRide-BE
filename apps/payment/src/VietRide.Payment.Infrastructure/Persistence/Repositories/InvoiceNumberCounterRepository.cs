using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using VietRide.Payment.Application.Abstractions.Repositories;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class InvoiceNumberCounterRepository : IInvoiceNumberCounterRepository
{
    private readonly PaymentDbContext _db;
    public InvoiceNumberCounterRepository(PaymentDbContext db) => _db = db;

    public async Task<long> NextAsync(string periodKey, CancellationToken cancellationToken)
    {
        if (periodKey.Length != 6 || !periodKey.All(char.IsDigit))
            throw new ArgumentException("Invoice period key must use yyyyMM.", nameof(periodKey));
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Invoice number allocation requires an active database transaction.");

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = $"""
            INSERT INTO {PaymentDbContext.SchemaName}.invoice_number_counters (period_key, last_value)
            VALUES (@periodKey, 1)
            ON CONFLICT (period_key) DO UPDATE
            SET last_value = {PaymentDbContext.SchemaName}.invoice_number_counters.last_value + 1
            RETURNING last_value
            """;
        command.Parameters.Add(new NpgsqlParameter<string>("periodKey", periodKey));
        var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (value > 999_999)
            throw new InvalidOperationException("INVOICE_NUMBER_EXHAUSTED");
        return value;
    }
}
