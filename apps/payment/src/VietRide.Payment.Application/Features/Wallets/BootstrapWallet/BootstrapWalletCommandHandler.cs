using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Wallets.BootstrapWallet;

public sealed class BootstrapWalletCommandHandler : IIntegrationEventHandler<UserCreatedIntegrationEvent>
{
    private readonly IWalletRepository _wallets;
    private readonly ILogger<BootstrapWalletCommandHandler> _logger;

    public BootstrapWalletCommandHandler(
        IWalletRepository wallets,
        ILogger<BootstrapWalletCommandHandler> logger)
    {
        _wallets = wallets;
        _logger = logger;
    }

    public async Task Handle(
        BootstrapWalletCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(request));

        var inserted = await _wallets.EnsureBootstrapWalletAsync(request.UserId, cancellationToken);

        if (inserted)
        {
            _logger.LogInformation(
                "Bootstrapped wallet for user {UserId} from {EventType}.",
                request.UserId,
                UserCreatedIntegrationEvent.EventType);
        }
        else
        {
            _logger.LogDebug(
                "Wallet bootstrap idempotent no-op for user {UserId} from {EventType}.",
                request.UserId,
                UserCreatedIntegrationEvent.EventType);
        }
    }

    public async Task HandleAsync(
        UserCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var command = new BootstrapWalletCommand(
            integrationEvent.UserId,
            integrationEvent.Role,
            integrationEvent.Email,
            integrationEvent.CreatedAt);

        await Handle(command, cancellationToken);
    }
}
