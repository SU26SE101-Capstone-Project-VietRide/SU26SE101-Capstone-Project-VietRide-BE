using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Shared.Persistence.Inbox;

public sealed class EfIntegrationEventInbox<TContext> : IIntegrationEventInbox
    where TContext : VietRideDbContextBase
{
    private readonly TContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public EfIntegrationEventInbox(TContext dbContext, IUnitOfWork unitOfWork, IClock clock)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public Task<IntegrationEventInboxResult> ExecuteAsync(
        string consumerName,
        Guid messageId,
        string payloadHash,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var existing = await _dbContext.Set<IntegrationInboxRecord>()
                .SingleOrDefaultAsync(
                    entry => entry.ConsumerName == consumerName && entry.MessageId == messageId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new IntegrationEventPayloadMismatchException(consumerName, messageId);

                return IntegrationEventInboxResult.Duplicate;
            }

            await handler(cancellationToken).ConfigureAwait(false);
            await _dbContext.Set<IntegrationInboxRecord>().AddAsync(
                IntegrationInboxRecord.Create(
                    consumerName,
                    messageId,
                    payloadHash,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);

            return IntegrationEventInboxResult.Processed;
        }, cancellationToken);
}
