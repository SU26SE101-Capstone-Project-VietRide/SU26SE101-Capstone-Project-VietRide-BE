namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record PassengerTrackingTargetDto(
    string Kind,
    Guid? StopId = null,
    Guid? StationId = null);
