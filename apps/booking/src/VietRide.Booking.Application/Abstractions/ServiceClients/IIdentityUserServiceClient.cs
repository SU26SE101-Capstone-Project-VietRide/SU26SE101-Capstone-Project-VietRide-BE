namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IIdentityUserServiceClient
{
    Task<Guid?> GetUserIdByPhoneAsync(string phone, CancellationToken cancellationToken = default);
}
