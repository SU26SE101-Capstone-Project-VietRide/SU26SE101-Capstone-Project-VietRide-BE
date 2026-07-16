using System.Net.Http.Json;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

public sealed class BookingImpactClient(HttpClient httpClient) : IBookingImpactClient
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

    public async Task<TripBookingImpactProjection> GetTripEditImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id must be non-empty.", nameof(operatorId));
        }

        var response = await httpClient.GetFromJsonAsync<TripBookingImpactProjection>(
            $"/internal/v1/bookings/trips/{tripId:D}/edit-impact?operatorId={operatorId:D}",
            cancellationToken);

        return IsValid(response, tripId)
            ? response!
            : throw new HttpRequestException("Booking Trip-edit impact returned invalid data.");
    }

    private static bool IsValid(TripBookingImpactProjection? response, Guid expectedTripId)
    {
        if (response is null
            || response.TripId != expectedTripId
            || response.ActiveBookings is null
            || response.ActiveBookingCount != response.ActiveBookings.Count)
        {
            return false;
        }

        var bookingIds = new HashSet<Guid>();
        foreach (var booking in response.ActiveBookings)
        {
            if (booking.BookingId == Guid.Empty
                || !bookingIds.Add(booking.BookingId)
                || (booking.Status != "PENDING_PAYMENT" && booking.Status != "CONFIRMED")
                || booking.SeatNumbers is null
                || booking.SeatNumbers.Any(string.IsNullOrWhiteSpace)
                || booking.SeatNumbers.Distinct(StringComparer.Ordinal).Count() != booking.SeatNumbers.Count)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record CountResponse(int ActiveBookingCount);
}
