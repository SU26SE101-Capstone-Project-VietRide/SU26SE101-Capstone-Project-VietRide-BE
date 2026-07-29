namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IIdentityUserServiceClient
{
    Task<Guid?> GetUserIdByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, BookingBuyerSnapshotProfile>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, BookingBuyerSnapshotProfile>>(
            new Dictionary<Guid, BookingBuyerSnapshotProfile>());
}
