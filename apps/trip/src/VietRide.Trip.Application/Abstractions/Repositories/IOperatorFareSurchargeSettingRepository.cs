using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IOperatorFareSurchargeSettingRepository : IRepository<OperatorFareSurchargeSetting, Guid>
{
    Task<OperatorFareSurchargeSetting?> GetByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);
}
