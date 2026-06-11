using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Features.Vehicles;

public static class SeatLayoutValidator
{
    public static void Validate(SeatLayoutDto? seatLayout, int requestTotalSeats)
    {
        var errors = new List<ValidationError>();

        if (seatLayout is null)
        {
            errors.Add(new ValidationError("seatLayoutJson", "Seat layout JSON is required."));
        }
        else
        {
            var seats = seatLayout.Seats ?? [];

            if (seatLayout.TotalSeats != seats.Count)
            {
                errors.Add(new ValidationError(
                    "seatLayoutJson.totalSeats",
                    "Seat layout totalSeats must equal seats length."));
            }

            if (seatLayout.TotalSeats != requestTotalSeats)
            {
                errors.Add(new ValidationError(
                    "totalSeats",
                    "Request totalSeats must equal seat layout totalSeats."));
            }

            var hasDuplicateSeatNumber = seats
                .GroupBy(seat => seat.SeatNumber, StringComparer.Ordinal)
                .Any(group => group.Count() > 1);

            if (hasDuplicateSeatNumber)
            {
                errors.Add(new ValidationError(
                    "seatLayoutJson.seats[].seatNumber",
                    "Seat numbers must be unique within the vehicle."));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Vehicle seat layout is invalid.", errors);
        }
    }
}
