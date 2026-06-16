using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Wallets.GetWallet;

public sealed class GetWalletQueryHandler : IRequestHandler<GetWalletQuery, GetWalletResult>
{
    private readonly IWalletRepository _wallets;

    public GetWalletQueryHandler(IWalletRepository wallets)
    {
        _wallets = wallets;
    }

    public async Task<GetWalletResult> Handle(GetWalletQuery request, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetUserWalletAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Wallet", request.UserId);

        return new GetWalletResult(
            wallet.UserId,
            wallet.Balance.Amount,
            wallet.Currency);
    }
}
