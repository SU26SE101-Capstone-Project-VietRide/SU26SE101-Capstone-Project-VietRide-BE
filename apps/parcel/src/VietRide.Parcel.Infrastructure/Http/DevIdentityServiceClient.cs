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

    public Task<RecipientUserLookupOutcome> FindUserByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Identity stub for FindUserByEmailAsync({Email}).", email);
        return Task.FromResult(RecipientUserLookupOutcome.NotFound());
    }

    public Task<IdentityUserBatchOutcome> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Any(userId => userId == Guid.Empty))
            throw new ArgumentException("User ids cannot contain an empty UUID.", nameof(userIds));

        var distinctUserIds = userIds.Distinct().ToArray();
        if (distinctUserIds.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(userIds), "At most 100 distinct user ids are allowed.");

        var users = distinctUserIds
            .Select(userId => new IdentityUserSummary(
                userId,
                "Dev Passenger",
                "+84901234567",
                "passenger@example.test",
                null,
                "PASSENGER",
                null,
                "ACTIVE",
                false))
            .ToArray();
        return Task.FromResult(IdentityUserBatchOutcome.Success(users));
    }

    public Task<OperatorLookupOutcome> GetOperatorInfoAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Identity stub for GetOperatorInfoAsync({OperatorId}).", operatorId);

        var opInfo = new IdentityOperatorInfo(
            Id: operatorId,
            Name: "Dev Operator",
            ParcelNoShowPolicy: ParcelNoShowPolicy.Default);

        return Task.FromResult(new OperatorLookupOutcome(OperatorLookupOutcomeKind.Success, opInfo, null));
    }
}
