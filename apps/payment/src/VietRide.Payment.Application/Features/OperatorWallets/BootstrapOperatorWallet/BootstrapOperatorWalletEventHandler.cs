using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.OperatorWallets.BootstrapOperatorWallet;

public sealed class BootstrapOperatorWalletEventHandler
    : IIntegrationEventHandler<OperatorApprovedConsumerEvent>
{
    private readonly IOperatorWalletRepository _wallets;

    public BootstrapOperatorWalletEventHandler(IOperatorWalletRepository wallets)
    {
        _wallets = wallets;
    }

    public async Task HandleAsync(
        OperatorApprovedConsumerEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.EventId == Guid.Empty || integrationEvent.OperatorId == Guid.Empty)
            throw new InvalidOperationException("Operator approval event identity is invalid.");

        var wallet = await _wallets.FindByOperatorIdAsync(
            integrationEvent.OperatorId,
            cancellationToken);
        if (wallet is null)
        {
            await _wallets.AddAsync(
                OperatorWallet.Create(integrationEvent.OperatorId),
                cancellationToken);
        }
    }
}
