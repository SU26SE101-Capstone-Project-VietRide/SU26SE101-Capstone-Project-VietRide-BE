using System.Security.Cryptography;
using System.Text;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Application.Features.Trips.Operations;

internal static class VehicleSubstitutionPreviewToken
{
    public static string Create(
        Guid tripId,
        int tripRowVersion,
        DateTimeOffset tripUpdatedAt,
        Guid replacementVehicleId,
        int vehicleRowVersion,
        DateTimeOffset vehicleUpdatedAt,
        VehicleSubstitutionImpactProjection impact,
        IReadOnlyCollection<string> seatNumbers)
    {
        var passengers = impact.Bookings
            .OrderBy(booking => booking.BookingId)
            .SelectMany(booking => booking.Passengers
                .OrderBy(passenger => passenger.PassengerId)
                .Select(passenger => string.Join(
                    ":",
                    booking.BookingId,
                    booking.BookingStatus,
                    passenger.PassengerId,
                    passenger.BoardingStatus,
                    Normalize(passenger.OriginalSeatNumber))));
        var canonical = string.Join(
            "|",
            tripId,
            tripRowVersion,
            tripUpdatedAt.UtcTicks,
            replacementVehicleId,
            vehicleRowVersion,
            vehicleUpdatedAt.UtcTicks,
            string.Join(",", seatNumbers.OrderBy(seat => seat, StringComparer.Ordinal)),
            string.Join(",", passengers));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Normalize(string? seatNumber)
        => string.IsNullOrWhiteSpace(seatNumber)
            ? string.Empty
            : seatNumber.Trim().ToUpperInvariant();
}
