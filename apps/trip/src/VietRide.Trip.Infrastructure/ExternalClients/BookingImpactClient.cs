using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

public sealed class BookingImpactClient(HttpClient httpClient) : IBookingImpactClient
{
    public async Task<TripStopPendingPassengerCountProjection> GetPendingPassengerCountAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        ValidateId(tripId, nameof(tripId));
        ValidateId(stopId, nameof(stopId));
        ValidateId(operatorId, nameof(operatorId));

        using var response = await httpClient.GetAsync(
            $"/internal/v1/bookings/trips/{tripId:D}/stops/{stopId:D}/pending-passenger-count?operatorId={operatorId:D}",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var properties = root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (!properties.SetEquals(["tripId", "stopId", "pendingPassengerCount"])
                || !root.TryGetProperty("tripId", out var responseTripId)
                || !responseTripId.TryGetGuid(out var parsedTripId)
                || parsedTripId != tripId
                || !root.TryGetProperty("stopId", out var responseStopId)
                || !responseStopId.TryGetGuid(out var parsedStopId)
                || parsedStopId != stopId
                || !root.TryGetProperty("pendingPassengerCount", out var countElement)
                || !countElement.TryGetInt32(out var count)
                || count < 0)
            {
                throw new HttpRequestException("Booking pending-passenger count returned invalid data.");
            }

            return new TripStopPendingPassengerCountProjection(parsedTripId, parsedStopId, count);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException(
                "Booking pending-passenger count returned invalid data.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new HttpRequestException(
                "Booking pending-passenger count returned invalid data.",
                exception);
        }
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

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value must be non-empty.", parameterName);
        }
    }
}
