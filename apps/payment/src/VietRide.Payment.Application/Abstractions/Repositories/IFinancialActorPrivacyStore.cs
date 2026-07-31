namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IFinancialActorPrivacyStore
{
    Task<bool> IsDeletedWithLockAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<int> MarkDeletedAndRedactAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
