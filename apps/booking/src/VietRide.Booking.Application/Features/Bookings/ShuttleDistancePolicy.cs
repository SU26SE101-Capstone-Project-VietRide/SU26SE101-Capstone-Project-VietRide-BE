using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Exceptions;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Bookings;

public static class ShuttleDistancePolicy
{
    public const int MaxDistanceMeters = 5_000;

    public static int Resolve(ShuttleRoadDistanceOutcome outcome)
        => outcome switch
        {
            ShuttleRoadDistanceOutcome.Success success when success.DistanceMeters <= MaxDistanceMeters
                => success.DistanceMeters,
            ShuttleRoadDistanceOutcome.Success success => throw new CodedValidationException(
                "SHUTTLE_DISTANCE_EXCEEDED",
                $"Shuttle road distance {success.DistanceMeters}m exceeds the {MaxDistanceMeters}m limit."),
            ShuttleRoadDistanceOutcome.Rejected rejected => throw new CodedValidationException(
                rejected.ErrorCode,
                rejected.Message),
            ShuttleRoadDistanceOutcome.Unavailable unavailable => throw new ShuttleDistanceUnavailableException(unavailable.Message),
            _ => throw new ShuttleDistanceUnavailableException("Shuttle road distance is unavailable."),
        };
}
