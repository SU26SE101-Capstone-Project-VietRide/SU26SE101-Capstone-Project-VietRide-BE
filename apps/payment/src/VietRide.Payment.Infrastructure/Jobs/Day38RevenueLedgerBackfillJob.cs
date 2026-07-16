using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class Day38RevenueLedgerBackfillOptions
{
    public const string SectionName = "RevenueLedgerBackfill";

    public bool Enabled { get; set; }
    public DateTimeOffset? LegacyCutoffUtc { get; set; }
    public int MaxBatchSize { get; set; } = 100;
}

public sealed class Day38RevenueLedgerBackfillJob
{
    public const string RecurringJobId = "payment.day38-revenue-ledger-backfill";

    private readonly PaymentDbContext _db;
    private readonly IOperatorLedgerEntryRepository _ledger;
    private readonly IRevenueLedgerWriter _writer;
    private readonly Day38RevenueLedgerBackfillOptions _options;
    private readonly ILogger<Day38RevenueLedgerBackfillJob> _logger;

    public Day38RevenueLedgerBackfillJob(
        PaymentDbContext db,
        IOperatorLedgerEntryRepository ledger,
        IRevenueLedgerWriter writer,
        IOptions<Day38RevenueLedgerBackfillOptions> options,
        ILogger<Day38RevenueLedgerBackfillJob> logger)
    {
        _db = db;
        _ledger = ledger;
        _writer = writer;
        _options = options.Value;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.LegacyCutoffUtc.HasValue)
            return;

        var cutoff = _options.LegacyCutoffUtc.Value;
        var batchSize = Math.Clamp(_options.MaxBatchSize, 1, 1_000);
        var payments = await _db.Payments
            .AsNoTracking()
            .Where(payment => payment.Status == PaymentStatus.SUCCEEDED
                && payment.CreatedAt < cutoff
                && payment.Context != "{}")
            .Where(payment => !_ledger.Query().Any(entry =>
                entry.SourceEventId == payment.Id))
            .OrderBy(payment => payment.CreatedAt)
            .ThenBy(payment => payment.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        foreach (var payment in payments)
        {
            var context = PaymentContextCodec.DeserializeTrusted(payment.Context);
            await _writer.RecordPaymentSucceededAsync(
                payment.Id,
                context,
                cancellationToken);
        }

        if (payments.Length > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Day 38 revenue ledger backfill wrote {PaymentCount} legacy payment(s) with deterministic source ids.",
            payments.Length);
    }
}
