using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public static class VehicleSubstitutionSeatAssignmentPolicy
{
    public static IReadOnlyList<SubstituteVehicleSeatPreview> CreatePreview(
        VehicleSubstitutionImpactProjection impact,
        IReadOnlyCollection<string> usableSeatNumbers)
    {
        var available = usableSeatNumbers
            .Select(NormalizeSeatNumber)
            .ToHashSet(StringComparer.Ordinal);
        var orderedPassengers = impact.Bookings
            .OrderBy(booking => booking.BookingId)
            .SelectMany(booking => booking.Passengers
                .OrderBy(passenger => passenger.PassengerId)
                .Select(passenger => (booking.BookingId, Passenger: passenger)))
            .ToArray();
        var exactByPassenger = new Dictionary<Guid, string>();
        foreach (var item in orderedPassengers)
        {
            var original = NormalizeNullableSeat(item.Passenger.OriginalSeatNumber);
            if (original is not null && available.Remove(original))
                exactByPassenger[item.Passenger.PassengerId] = original;
        }

        var alternatives = available.OrderBy(seat => seat, StringComparer.Ordinal).ToArray();
        return orderedPassengers.Select(item =>
        {
            var exact = exactByPassenger.GetValueOrDefault(item.Passenger.PassengerId);
            return new SubstituteVehicleSeatPreview(
                item.BookingId,
                item.Passenger.PassengerId,
                NormalizeNullableSeat(item.Passenger.OriginalSeatNumber),
                exact,
                exact is null,
                exact is null ? alternatives : []);
        }).ToArray();
    }

    public static IReadOnlyDictionary<Guid, string> Resolve(
        VehicleSubstitutionImpactProjection impact,
        IReadOnlyCollection<string> usableSeatNumbers,
        IReadOnlyList<SubstituteVehicleSeatAssignment>? requestedAssignments,
        string? previewToken,
        string expectedPreviewToken)
    {
        var previews = CreatePreview(impact, usableSeatNumbers);
        var requestedGroups = (requestedAssignments ?? []).GroupBy(item => item.PassengerId).ToArray();
        if (requestedGroups.Any(group => group.Count() != 1))
            throw SeatNotAvailable("Each Passenger may have only one replacement seat assignment.");

        var requested = requestedGroups.ToDictionary(
            group => group.Key,
            group => NormalizeSeatNumber(group.Single().NewSeatNumber));
        var passengerIds = previews.Select(item => item.PassengerId).ToHashSet();
        if (requested.Keys.Any(id => !passengerIds.Contains(id)))
            throw SeatNotAvailable("Seat assignment contains an unknown Passenger.");

        var result = previews
            .Where(item => item.ProposedSeatNumber is not null)
            .ToDictionary(item => item.PassengerId, item => item.ProposedSeatNumber!);
        var usedSeats = result.Values.ToHashSet(StringComparer.Ordinal);
        var missing = previews.Where(item => item.RequiresAdminSelection).ToArray();
        if (missing.Length == 0)
            return result;

        if (string.IsNullOrWhiteSpace(previewToken))
            throw AssignmentRequired("PreviewToken is required when an exact seat cannot be preserved.");
        if (!string.Equals(previewToken, expectedPreviewToken, StringComparison.Ordinal))
        {
            throw new CodedConflictException(
                "REPLACEMENT_SEAT_PREVIEW_STALE",
                "Seat preview is stale. Please preview the replacement vehicle again.");
        }

        var usable = usableSeatNumbers.Select(NormalizeSeatNumber).ToHashSet(StringComparer.Ordinal);
        foreach (var passenger in missing)
        {
            if (!requested.TryGetValue(passenger.PassengerId, out var selected))
                throw AssignmentRequired($"Passenger {passenger.PassengerId} requires an Admin-selected replacement seat.");
            if (!usable.Contains(selected) || !usedSeats.Add(selected))
                throw SeatNotAvailable($"Replacement seat '{selected}' is not available.");
            result[passenger.PassengerId] = selected;
        }

        return result;
    }

    private static CodedConflictException AssignmentRequired(string message)
        => new("REPLACEMENT_SEAT_ASSIGNMENT_REQUIRED", message);

    private static CodedConflictException SeatNotAvailable(string message)
        => new("REPLACEMENT_SEAT_NOT_AVAILABLE", message);

    private static string NormalizeSeatNumber(string? seatNumber)
    {
        var normalized = seatNumber?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length is < 1 or > 20)
            throw SeatNotAvailable("Replacement seat number must contain 1 to 20 characters.");
        return normalized;
    }

    private static string? NormalizeNullableSeat(string? seatNumber)
        => string.IsNullOrWhiteSpace(seatNumber) ? null : NormalizeSeatNumber(seatNumber);
}
