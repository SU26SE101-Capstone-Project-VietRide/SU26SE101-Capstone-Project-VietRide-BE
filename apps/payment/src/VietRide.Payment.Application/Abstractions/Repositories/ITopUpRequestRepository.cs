using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface ITopUpRequestRepository : IRepository<TopUpRequest, Guid>
{
    Task<TopUpRequest?> FindByVnPayTxnRefAsync(string vnPayTxnRef, CancellationToken cancellationToken);
}
