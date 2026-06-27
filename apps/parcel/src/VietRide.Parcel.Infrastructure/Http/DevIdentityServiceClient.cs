using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class DevIdentityServiceClient : IIdentityServiceClient
{
    private readonly ILogger<DevIdentityServiceClient> _logger;

    public DevIdentityServiceClient(ILogger<DevIdentityServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<UserLookupOutcome> GetUserInfoAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Identity stub for GetUserInfoAsync({UserId}).", userId);

        var userInfo = new IdentityUserInfo(
            Id: userId,
            Role: "PASSENGER",
            OperatorId: null,
            Status: "ACTIVE");

        return Task.FromResult(new UserLookupOutcome(UserLookupOutcomeKind.Success, userInfo, null));
    }

    public Task<OperatorLookupOutcome> GetOperatorInfoAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Identity stub for GetOperatorInfoAsync({OperatorId}).", operatorId);

        var opInfo = new IdentityOperatorInfo(
            Id: operatorId,
            Name: "Dev Operator");

        return Task.FromResult(new OperatorLookupOutcome(OperatorLookupOutcomeKind.Success, opInfo, null));
    }
}
