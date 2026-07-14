using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Jobs;

public sealed class OperatorWalletBackfillJob
{
    public const string RecurringJobId = "identity.operator-wallet-backfill";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IdentityDbContext _db;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<OperatorWalletBackfillJob> _logger;

    public OperatorWalletBackfillJob(
        IdentityDbContext db,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<OperatorWalletBackfillJob> logger)
    {
        _db = db;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var operatorIds = await _db.Operators
            .AsNoTracking()
            .Where(operatorEntity =>
                operatorEntity.RegistrationStatus == OperatorRegistrationStatus.APPROVED
                && !_db.OperatorWalletBackfillMarkers.Any(marker =>
                    marker.OperatorId == operatorEntity.Id))
            .OrderBy(operatorEntity => operatorEntity.Id)
            .Select(operatorEntity => operatorEntity.Id)
            .Take(500)
            .ToArrayAsync(cancellationToken);

        foreach (var operatorId in operatorIds)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var marker = await _db.OperatorWalletBackfillMarkers
                    .SingleOrDefaultAsync(item => item.OperatorId == operatorId, cancellationToken);
                if (marker is null)
                {
                    marker = OperatorWalletBackfillMarker.Create(operatorId, Guid.NewGuid());
                    await _db.OperatorWalletBackfillMarkers.AddAsync(marker, cancellationToken);
                }

                var approvedAt = _clock.UtcNow;
                var evt = new OperatorApprovedIntegrationEvent(
                    marker.EventId,
                    operatorId,
                    approvedAt);
                await _outbox.EnqueueAsync(
                    OperatorApprovedIntegrationEvent.EventType,
                    JsonSerializer.Serialize(evt, JsonOptions),
                    cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogDebug(
                    exception,
                    "Operator wallet backfill marker race for operator {OperatorId}; retry will reuse the durable winner.",
                    operatorId);
            }
        }
    }
}
