using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.OperatorWallets.BootstrapOperatorWallet;

public sealed class BootstrapOperatorWalletEventHandler
    : IIntegrationEventHandler<OperatorApprovedConsumerEvent>
{
    private const string ConsumerName = "payment.operator-wallet-bootstrap";

    private readonly IOperatorWalletRepository _wallets;
    private readonly IProcessedIntegrationEventRepository _processedEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public BootstrapOperatorWalletEventHandler(
        IOperatorWalletRepository wallets,
        IProcessedIntegrationEventRepository processedEvents,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _wallets = wallets;
        _processedEvents = processedEvents;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task HandleAsync(
        OperatorApprovedConsumerEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.EventId == Guid.Empty || integrationEvent.OperatorId == Guid.Empty)
            throw new InvalidOperationException("Operator approval event identity is invalid.");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await _processedEvents.ExistsAsync(
                    ConsumerName,
                    integrationEvent.EventId,
                    cancellationToken))
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return;
            }

            var wallet = await _wallets.FindByOperatorIdAsync(
                integrationEvent.OperatorId,
                cancellationToken);
            if (wallet is null)
            {
                await _wallets.AddAsync(
                    OperatorWallet.Create(integrationEvent.OperatorId),
                    cancellationToken);
            }

            await _processedEvents.AddAsync(
                ProcessedIntegrationEvent.Create(
                    ConsumerName,
                    integrationEvent.EventId,
                    _clock.UtcNow),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
