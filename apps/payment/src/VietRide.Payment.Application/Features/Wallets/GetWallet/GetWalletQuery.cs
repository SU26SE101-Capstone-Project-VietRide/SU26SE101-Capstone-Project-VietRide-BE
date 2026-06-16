using MediatR;

namespace VietRide.Payment.Application.Features.Wallets.GetWallet;

public sealed record GetWalletQuery(Guid UserId) : IRequest<GetWalletResult>;
