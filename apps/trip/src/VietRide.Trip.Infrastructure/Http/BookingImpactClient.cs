using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.Http;

/// <summary>Booking impact adapter kept under the HTTP boundary for operator trip edits.</summary>
public sealed class BookingImpactClient : IBookingImpactClient
{
    private readonly HttpClient httpClient;
    private readonly BookingImpactClientOptions options;
    private readonly ExternalClients.BookingImpactClient legacy;

    public BookingImpactClient(HttpClient httpClient, IOptions<BookingImpactClientOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        legacy = new ExternalClients.BookingImpactClient(httpClient);
        httpClient.Timeout = this.options.Timeout;
    }

    public Task<TripStopPendingPassengerCountProjection> GetPendingPassengerCountAsync(
        Guid tripId, Guid stopId, Guid operatorId, CancellationToken cancellationToken)
        => legacy.GetPendingPassengerCountAsync(tripId, stopId, operatorId, cancellationToken);

    public async Task<TripBookingImpactProjection> GetTripEditImpactAsync(
        Guid tripId, Guid operatorId, CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty || operatorId == Guid.Empty)
            throw new ArgumentException("Trip and operator ids are required.");
        var path = options.ImpactPath.Replace("{tripId}", tripId.ToString("D"), StringComparison.Ordinal)
            + $"?operatorId={operatorId:D}";
        TripBookingImpactProjection? response;
        try
        {
            response = await httpClient.GetFromJsonAsync<TripBookingImpactProjection>(
                path,
                cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new HttpRequestException(
                "Booking Trip-edit impact returned malformed JSON.",
                exception);
        }

        if (response is null
            || response.TripId != tripId
            || response.ActiveBookings is null
            || response.ActiveBookingCount != response.ActiveBookings.Count
            || response.ActiveBookings
                .GroupBy(booking => booking.BookingId)
                .Any(group => group.Count() > 1)
            || response.ActiveBookings.Any(booking =>
                booking.BookingId == Guid.Empty
                || (booking.Status != "PENDING_PAYMENT" && booking.Status != "CONFIRMED")
                || booking.SeatNumbers is null
                || booking.SeatNumbers.Any(string.IsNullOrWhiteSpace)
                || booking.SeatNumbers
                    .GroupBy(seatNumber => seatNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1)))
            throw new HttpRequestException("Booking Trip-edit impact returned invalid data.");
        return response;
    }

    public async Task<VehicleSubstitutionImpactProjection> GetVehicleSubstitutionImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (tripId == Guid.Empty || operatorId == Guid.Empty)
        {
            throw new ArgumentException("Trip and operator ids are required.");
        }

        var path = options.VehicleSubstitutionImpactPath
            .Replace("{tripId}", tripId.ToString("D"), StringComparison.Ordinal)
            + $"?operatorId={operatorId:D}";
        VehicleSubstitutionImpactProjection? response;
        try
        {
            response = await httpClient.GetFromJsonAsync<VehicleSubstitutionImpactProjection>(
                path,
                cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new HttpRequestException(
                "Booking vehicle-substitution impact returned malformed JSON.",
                exception);
        }

        if (response is null
            || response.OldTripId != tripId
            || response.OperatorId != operatorId
            || response.Bookings is null
            || response.Bookings.Any(booking =>
                booking.BookingId == Guid.Empty
                || booking.BookingStatus is not ("CONFIRMED" or "PARTIAL_NO_SHOW")
                || booking.Passengers is null
                || booking.Passengers.Any(passenger =>
                    passenger.PassengerId == Guid.Empty
                    || passenger.BoardingStatus is not ("BOARDED" or "PENDING")))
            || response.Bookings.Select(booking => booking.BookingId).Distinct().Count()
                != response.Bookings.Count
            || response.Bookings.SelectMany(booking => booking.Passengers)
                .Select(passenger => passenger.PassengerId).Distinct().Count()
                != response.Bookings.Sum(booking => booking.Passengers.Count))
        {
            throw new HttpRequestException("Booking vehicle-substitution impact returned invalid data.");
        }

        return response with
        {
            Bookings = response.Bookings
                .OrderBy(booking => booking.BookingId)
                .Select(booking => booking with
                {
                    Passengers = booking.Passengers.OrderBy(passenger => passenger.PassengerId).ToArray(),
                })
                .ToArray(),
        };
    }
}
