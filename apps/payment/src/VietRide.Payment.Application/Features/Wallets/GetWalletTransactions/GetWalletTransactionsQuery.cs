using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Application.Features.Wallets.GetWalletTransactions;

public sealed record GetWalletTransactionsQuery(
    Guid UserId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Type,
    int Page,
    int PageSize) : IRequest<PagedResult<GetWalletTransactionResult>>;
