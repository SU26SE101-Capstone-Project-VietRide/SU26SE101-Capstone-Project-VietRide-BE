using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IStationRepository : IRepository<Station, Guid>
{
    Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
        string? q,
        string? city,
        string? province,
        Guid? locationId,
        CancellationToken cancellationToken);
}
