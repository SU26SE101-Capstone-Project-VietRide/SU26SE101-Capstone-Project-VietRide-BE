namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed record TripFareBreakdownDto(
    long BaseFare,
    IReadOnlyList<TripFareStopDto> Stops);
