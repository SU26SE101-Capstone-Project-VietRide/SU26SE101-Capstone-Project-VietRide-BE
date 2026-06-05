using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IOAuthIdentityRepository : IRepository<OAuthIdentity, Guid>
{
    Task<User?> GetUserByProviderSubjectAsync(
        OAuthProvider provider,
        string providerSubject,
        CancellationToken cancellationToken = default);
}
