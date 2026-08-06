namespace VietRide.Trip.Application.Features.Vehicles;

public static class SeatLayoutMetrics
{
    public static int CountUsablePassengerSeats(SeatLayoutDto layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return layout.Seats.Count(IsUsablePassengerSeat);
    }

    public static bool IsUsablePassengerSeat(SeatLayoutSeatDto seat)
    {
        ArgumentNullException.ThrowIfNull(seat);

        return !seat.Disabled
            && !string.Equals(seat.Type, "DRIVER_AREA", StringComparison.OrdinalIgnoreCase);
    }
}
