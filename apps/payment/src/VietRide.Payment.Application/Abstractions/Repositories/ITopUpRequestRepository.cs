using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface ITopUpRequestRepository : IRepository<TopUpRequest, Guid>
{
    Task<TopUpRequest?> FindByVnPayTxnRefAsync(string vnPayTxnRef, CancellationToken cancellationToken);

    Task<TopUpRequest?> FindPendingByVnPayTxnRefForUpdateAsync(
        string vnPayTxnRef,
        CancellationToken cancellationToken)
        => FindByVnPayTxnRefAsync(vnPayTxnRef, cancellationToken);

    Task<int> ExpirePendingOlderThanAsync(
        DateTimeOffset expiresBefore,
        DateTimeOffset expiredAt,
        CancellationToken cancellationToken)
    {
        var expired = Query()
            .Where(topUp => topUp.Status == TopUpRequestStatus.PENDING && topUp.CreatedAt < expiresBefore)
            .ToList();

        foreach (var topUp in expired)
        {
            topUp.MarkExpired(expiredAt);
            Update(topUp);
        }

        return Task.FromResult(expired.Count);
    }
}
