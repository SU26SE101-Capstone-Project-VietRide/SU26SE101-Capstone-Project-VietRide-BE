using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Application.Features.Bookings.History;

public static class BookingHistoryVehicleMapping
{
    public static BookingHistoryVehicleDto? FromSummary(TripHistoryVehicleSummary? summary)
    {
        if (summary is null || string.IsNullOrWhiteSpace(summary.LicensePlate))
            return null;

        return new BookingHistoryVehicleDto(
            summary.LicensePlate,
            summary.VehicleType is null
                ? null
                : new BookingHistoryVehicleTypeDto(
                    summary.VehicleType.Code,
                    summary.VehicleType.DisplayName));
    }
}
