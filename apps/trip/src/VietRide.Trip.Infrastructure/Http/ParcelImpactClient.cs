using System.Net.Http.Json;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.Http;

public sealed class ParcelImpactClient(HttpClient httpClient) : IParcelImpactClient
{
    public async Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty || operatorId == Guid.Empty)
            throw new ArgumentException("Trip and operator ids must be non-empty.");

        var response = await httpClient.GetFromJsonAsync<TripParcelCancellationImpactProjection>(
            $"/internal/v1/parcels/trips/{tripId:D}/cancel-impact?operatorId={operatorId:D}",
            cancellationToken);
        if (response is null
            || response.TripId != tripId
            || response.AffectedParcels is null
            || response.AffectedParcels.Any(parcel =>
                parcel.ParcelId == Guid.Empty || parcel.RefundAmount < 0)
            || response.AffectedParcels.Select(parcel => parcel.ParcelId).Distinct().Count()
                != response.AffectedParcels.Count)
        {
            throw new HttpRequestException("Parcel Trip-cancellation impact returned invalid data.");
        }

        return response;
    }
}
