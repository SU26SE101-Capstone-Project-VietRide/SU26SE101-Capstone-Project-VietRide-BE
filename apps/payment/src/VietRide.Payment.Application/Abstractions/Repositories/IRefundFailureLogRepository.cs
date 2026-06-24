using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IRefundFailureLogRepository : IRepository<RefundFailureLog, Guid>
{
    Task<IReadOnlyList<RefundFailureLog>> GetUnresolvedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RefundFailureLog>> GetRetryableAsync(int maxRetryCount, CancellationToken cancellationToken);
}
