using System.Net.Http.Json;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class BookingImpactClient(HttpClient httpClient) : IBookingImpactClient
{
    public async Task<int> GetActiveBookingCountByStopAsync(
        Guid stopId, Guid operatorId, CancellationToken cancellationToken)
    {
        // Successful /internal responses are intentionally raw (ApiResponseResultFilter
        // only wraps public endpoints), so deserialize the count projection directly.
        var response = await httpClient.GetFromJsonAsync<CountResponse>(
            $"/internal/v1/bookings/active-by-stop/{stopId:D}/count?operatorId={operatorId:D}", cancellationToken);
        return response?.ActiveBookingCount
            ?? throw new HttpRequestException("Booking impact count returned no data.");
    }

    private sealed record CountResponse(int ActiveBookingCount);
}
