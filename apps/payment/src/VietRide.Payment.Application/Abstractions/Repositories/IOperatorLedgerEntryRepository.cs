using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IOperatorLedgerEntryRepository : IRepository<OperatorLedgerEntry, Guid>
{
    Task<long> SumTripNetAmountAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken);
}
