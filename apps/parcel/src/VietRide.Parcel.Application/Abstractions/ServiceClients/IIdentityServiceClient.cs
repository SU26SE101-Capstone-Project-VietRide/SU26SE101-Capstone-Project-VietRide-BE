namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IIdentityServiceClient
{
    Task<UserLookupOutcome> GetUserInfoAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OperatorLookupOutcome> GetOperatorInfoAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);
}
