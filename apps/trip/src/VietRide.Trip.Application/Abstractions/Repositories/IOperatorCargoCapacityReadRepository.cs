namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IOperatorCargoCapacityReadRepository
{
    Task<OperatorCargoCapacityReadModel?> GetAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);
}
