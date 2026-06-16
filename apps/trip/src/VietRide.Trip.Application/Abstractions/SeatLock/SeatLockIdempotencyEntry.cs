using System.Text.Json.Serialization;
using VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

namespace VietRide.Trip.Application.Abstractions.SeatLock;

public sealed record SeatLockIdempotencyEntry(
    string RequestFingerprint,
    IReadOnlyList<string> NormalizedSeatNumbers,
    LockSeatsResult? Result,
    string? ReservationToken = null)
{
    [JsonIgnore]
    public bool IsCompleted => Result is not null;
}
