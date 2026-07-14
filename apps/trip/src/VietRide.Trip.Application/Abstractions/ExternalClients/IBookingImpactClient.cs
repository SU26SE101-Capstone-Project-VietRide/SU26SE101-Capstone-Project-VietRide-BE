namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public interface IBookingImpactClient
{
    Task<int> GetActiveBookingCountByStopAsync(Guid stopId, Guid operatorId, CancellationToken cancellationToken);
}
