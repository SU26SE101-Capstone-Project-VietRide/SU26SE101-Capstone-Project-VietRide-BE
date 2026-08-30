using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class PreviewSubstituteVehicleQueryHandler
    : IRequestHandler<PreviewSubstituteVehicleQuery, SubstituteVehiclePreviewResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITripRepository trips;
    private readonly IVehicleRepository vehicles;
    private readonly IBookingImpactClient bookingImpact;

    public PreviewSubstituteVehicleQueryHandler(
        ITripRepository trips,
        IVehicleRepository vehicles,
        IBookingImpactClient bookingImpact)
    {
        this.trips = trips;
        this.vehicles = vehicles;
        this.bookingImpact = bookingImpact;
    }

    public async Task<SubstituteVehiclePreviewResponse> Handle(
        PreviewSubstituteVehicleQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await trips.QueryNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.TripId && item.OperatorId == request.OperatorId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var vehicle = await vehicles.GetOwnedByIdAsync(request.OperatorId, request.ReplacementVehicleId, cancellationToken)
            ?? throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Replacement vehicle was not found.");
        EnsureVehicleActive(vehicle);
        if (trip.VehicleId == vehicle.Id)
            throw new CodedConflictException("TRIP_VEHICLE_SAME_AS_OLD", "Replacement vehicle must differ from the old vehicle.");
        if (!TripVehicleSubstitutionPolicy.CanSubstitute(trip.Status))
            throw new CodedConflictException("TRIP_NOT_SUBSTITUTABLE", "Vehicle substitution requires an in-progress Trip.");

        var impact = await bookingImpact.GetVehicleSubstitutionImpactAsync(request.TripId, request.OperatorId, cancellationToken);
        var layout = ParsePassengerLayout(vehicle);
        var seatNumbers = layout.Select(seat => seat.SeatNumber).ToArray();
        var passengersToTransfer = impact.Bookings.SelectMany(booking => booking.Passengers).Count();
        if (passengersToTransfer > seatNumbers.Length)
        {
            throw new CodedConflictException(
                "REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS",
                "Replacement vehicle does not have enough usable seats.",
                [
                    new ValidationError("usableSeats", seatNumbers.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new ValidationError("passengersToTransfer", passengersToTransfer.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new ValidationError("missingSeats", (passengersToTransfer - seatNumbers.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ]);
        }
        var previews = VehicleSubstitutionSeatAssignmentPolicy.CreatePreview(impact, seatNumbers);

        var token = VehicleSubstitutionPreviewToken.Create(
            trip.Id,
            trip.RowVersion,
            trip.UpdatedAt,
            vehicle.Id,
            vehicle.RowVersion,
            vehicle.UpdatedAt,
            impact,
            seatNumbers);
        return new SubstituteVehiclePreviewResponse(
            trip.Id,
            vehicle.Id,
            token,
            previews,
            seatNumbers);
    }

    private static IReadOnlyList<LayoutSeat> ParsePassengerLayout(Vehicle vehicle)
    {
        var layout = vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>(JsonOptions)
            ?? throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout is invalid.");
        var result = new List<LayoutSeat>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in layout.Seats.OrderBy(item => item.SeatNumber, StringComparer.Ordinal))
        {
            var number = NormalizeNullableSeat(item.SeatNumber)
                ?? throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout contains a blank seat.");
            if (!seen.Add(number))
                throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout contains duplicate seats.");
            if (!Enum.TryParse<TripSeatType>(item.Type, true, out var seatType) || !Enum.IsDefined(seatType))
                throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout contains an unknown seat type.");
            if (!item.Disabled && seatType != TripSeatType.DRIVER_AREA)
                result.Add(new LayoutSeat(number, seatType));
        }
        return result;
    }

    private static void EnsureVehicleActive(Vehicle vehicle)
    {
        if (!vehicle.IsActive || vehicle.Status != VehicleStatus.ACTIVE || vehicle.DeletedAt.HasValue)
            throw new CodedValidationException("VEHICLE_NOT_ACTIVE", "Replacement vehicle must be active.");
    }

    private static string? NormalizeNullableSeat(string? seatNumber)
        => string.IsNullOrWhiteSpace(seatNumber) ? null : seatNumber.Trim().ToUpperInvariant();

    private sealed record LayoutSeat(string SeatNumber, TripSeatType SeatType);
}
