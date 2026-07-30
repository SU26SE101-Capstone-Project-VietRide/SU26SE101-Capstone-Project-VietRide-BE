namespace VietRide.Trip.Application.Abstractions.Repositories;

public enum TripCargoTransferStatus
{
    SUCCESS = 0,
    TRIP_NOT_FOUND = 1,
    SOURCE_CARGO_NOT_FOUND = 2,
    CONFLICT = 3,
    CAPACITY_EXCEEDED = 4,
    OVERFLOW_NOT_ALLOWED = 5,
}
