namespace VietRide.Trip.Domain.Entities;

public static class TripVehicleSubstitutionPolicy
{
    public static bool CanSubstitute(TripStatus status) => status == TripStatus.IN_PROGRESS;
}
