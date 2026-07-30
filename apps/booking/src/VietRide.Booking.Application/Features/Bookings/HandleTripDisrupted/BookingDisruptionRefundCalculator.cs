using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Exceptions;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Features.Bookings.HandleTripDisrupted;

public static class BookingDisruptionRefundCalculator
{
    public static BookingDisruptionRefund Calculate(BookingEntity booking, TripSnapshot trip)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(trip);
        EnsurePickupBelongsToTrip(booking, trip);

        if (booking.PickupStopId.HasValue
            && !string.Equals(
                FindPickupStop(booking, trip).Status,
                "ARRIVED",
                StringComparison.Ordinal))
        {
            return new BookingDisruptionRefund(0m, booking.TotalAmount.Amount);
        }

        var traveledRatio = TryCalculateDistanceRatio(booking, trip)
            ?? CalculateOrderRatio(booking, trip);
        traveledRatio = Math.Clamp(traveledRatio, 0m, 1m);

        var totalAmount = booking.TotalAmount.Amount;
        var rounded = decimal.Round(
            totalAmount * (1m - traveledRatio),
            0,
            MidpointRounding.AwayFromZero);
        var refundAmount = Math.Clamp(decimal.ToInt64(rounded), 0L, totalAmount);

        return new BookingDisruptionRefund(traveledRatio, refundAmount);
    }

    private static decimal? TryCalculateDistanceRatio(BookingEntity booking, TripSnapshot trip)
    {
        if (!TryToNonNegativeDecimal(trip.TotalDistanceKm, out var totalDistance))
        {
            return null;
        }

        decimal pickupDistance;
        if (booking.PickupStationId.HasValue)
        {
            pickupDistance = 0m;
        }
        else
        {
            var pickup = FindPickupStop(booking, trip);
            if (!TryToNonNegativeDecimal(pickup.DistanceFromOriginKm, out pickupDistance))
            {
                return null;
            }
        }

        var arrived = trip.Stops
            .Where(stop => string.Equals(stop.Status, "ARRIVED", StringComparison.Ordinal))
            .ToArray();
        decimal reachedDistance;
        if (arrived.Length == 0)
        {
            reachedDistance = 0m;
        }
        else
        {
            var distances = new decimal[arrived.Length];
            for (var index = 0; index < arrived.Length; index++)
            {
                if (!TryToNonNegativeDecimal(arrived[index].DistanceFromOriginKm, out distances[index]))
                {
                    return null;
                }
            }

            reachedDistance = distances.Max();
        }

        var bookingTotalDistance = totalDistance - pickupDistance;
        if (bookingTotalDistance <= 0m)
        {
            return 0m;
        }

        var bookingTravelDistance = Math.Max(reachedDistance - pickupDistance, 0m);
        return bookingTravelDistance / bookingTotalDistance;
    }

    private static decimal CalculateOrderRatio(BookingEntity booking, TripSnapshot trip)
    {
        if (trip.Stops.Any(stop => stop.OrderIndex <= 0))
        {
            throw new BookingUpstreamUnavailableException(
                "Trip snapshot contains an invalid stop order.");
        }

        var pickupOrder = booking.PickupStationId.HasValue
            ? 0
            : FindPickupStop(booking, trip).OrderIndex;
        var reachedOrder = trip.Stops
            .Where(stop => string.Equals(stop.Status, "ARRIVED", StringComparison.Ordinal))
            .Select(stop => stop.OrderIndex)
            .DefaultIfEmpty(0)
            .Max();
        var totalOrder = checked(
            trip.Stops.Select(stop => stop.OrderIndex).DefaultIfEmpty(0).Max() + 1);
        var bookingTotalOrder = totalOrder - pickupOrder;
        if (bookingTotalOrder <= 0)
        {
            return 0m;
        }

        var bookingTravelOrder = Math.Max(reachedOrder - pickupOrder, 0);
        return (decimal)bookingTravelOrder / bookingTotalOrder;
    }

    private static TripStopSnapshot FindPickupStop(BookingEntity booking, TripSnapshot trip)
    {
        if (!booking.PickupStopId.HasValue)
        {
            throw new BookingUpstreamUnavailableException(
                "Booking does not contain a usable pickup point.");
        }

        var matches = trip.Stops
            .Where(stop => stop.StopId == booking.PickupStopId.Value)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new BookingUpstreamUnavailableException(
                "Trip snapshot does not contain exactly one Booking pickup stop.");
        }

        return matches[0];
    }

    private static void EnsurePickupBelongsToTrip(BookingEntity booking, TripSnapshot trip)
    {
        if (booking.PickupStationId.HasValue
            && booking.PickupStationId.Value != trip.OriginStation.Id)
        {
            throw new BookingUpstreamUnavailableException(
                "Trip snapshot does not contain the Booking pickup station.");
        }

        if (booking.PickupStopId.HasValue)
        {
            _ = FindPickupStop(booking, trip);
        }
    }

    private static bool TryToNonNegativeDecimal(double? value, out decimal converted)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0d)
        {
            converted = 0m;
            return false;
        }

        converted = Convert.ToDecimal(value.Value);
        return true;
    }
}

public sealed record BookingDisruptionRefund(decimal TraveledRatio, long RefundAmount);
