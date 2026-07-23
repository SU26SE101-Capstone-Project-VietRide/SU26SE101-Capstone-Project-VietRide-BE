using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

internal sealed class PaymentIntegrationEventInbox : IIntegrationEventInbox
{
    private readonly PaymentDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public PaymentIntegrationEventInbox(
        PaymentDbContext dbContext,
        IUnitOfWork unitOfWork,
        IClock clock)
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
            var existing = await _dbContext.ProcessedIntegrationEvents
                .SingleOrDefaultAsync(
                    entry => entry.Consumer == consumerName && entry.EventId == messageId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(existing.PayloadHash)
                    && !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                {
                    throw new IntegrationEventPayloadMismatchException(consumerName, messageId);
                }

                return IntegrationEventInboxResult.Duplicate;
            }

            await handler(cancellationToken).ConfigureAwait(false);
            await _dbContext.ProcessedIntegrationEvents.AddAsync(
                ProcessedIntegrationEvent.Create(
                    consumerName,
                    messageId,
                    _clock.UtcNow,
                    payloadHash),
                cancellationToken).ConfigureAwait(false);

            return IntegrationEventInboxResult.Processed;
        }, cancellationToken);
}
