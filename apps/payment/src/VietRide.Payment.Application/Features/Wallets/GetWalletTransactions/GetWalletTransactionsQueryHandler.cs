using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Application.Features.Wallets.GetWalletTransactions;

public sealed class GetWalletTransactionsQueryHandler : IRequestHandler<GetWalletTransactionsQuery, PagedResult<GetWalletTransactionResult>>
{
    private readonly IWalletRepository _wallets;

    public GetWalletTransactionsQueryHandler(IWalletRepository wallets)
    {
        _wallets = wallets;
    }

    public Task<PagedResult<GetWalletTransactionResult>> Handle(
        GetWalletTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        WalletTransactionType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!Enum.GetNames<WalletTransactionType>().Any(name => string.Equals(name, request.Type, StringComparison.OrdinalIgnoreCase)))
            {
                throw new VietRide.Shared.Application.Exceptions.ValidationException(
                    "One or more validation errors occurred.",
                    [new VietRide.Shared.Application.Exceptions.ValidationError(nameof(request.Type), "type must be a valid wallet transaction type.")]);
            }

            if (!Enum.TryParse<WalletTransactionType>(request.Type, true, out var parsedType))
            {
                throw new VietRide.Shared.Application.Exceptions.ValidationException(
                    "One or more validation errors occurred.",
                    [new VietRide.Shared.Application.Exceptions.ValidationError(nameof(request.Type), "type must be a valid wallet transaction type.")]);
            }

            type = parsedType;
        }

        return _wallets.GetUserWalletTransactionsAsync(
            request.UserId,
            request.From,
            request.To,
            type,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}
