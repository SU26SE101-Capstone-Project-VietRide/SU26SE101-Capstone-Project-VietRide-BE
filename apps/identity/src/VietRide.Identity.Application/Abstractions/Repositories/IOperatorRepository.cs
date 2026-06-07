using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IOperatorRepository : IRepository<Operator, Guid>
{
    Task<Operator?> GetByBusinessRegistrationNumberAsync(
        string businessRegistrationNumber,
        CancellationToken cancellationToken = default);

    Task<Operator?> GetByTaxCodeAsync(
        string taxCode,
        CancellationToken cancellationToken = default);
}
