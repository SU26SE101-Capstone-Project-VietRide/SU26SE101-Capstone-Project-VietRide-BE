using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IOperatorFareSurchargePeriodRepository : IRepository<OperatorFareSurchargePeriod, Guid>
{
    Task<OperatorFareSurchargePeriod?> GetOwnedByIdAsync(
        Guid operatorId,
        Guid periodId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveOverlapAsync(
        Guid operatorId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludedPeriodId,
        CancellationToken cancellationToken = default);

    Task<OperatorFareSurchargePeriod?> GetActiveForDateAsync(
        Guid operatorId,
        DateOnly departureDate,
        CancellationToken cancellationToken = default);
}
