using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips;

public sealed class CancelTripPreviewQueryHandler
    : IRequestHandler<CancelTripPreviewQuery, CancelTripPreviewResponse>
{
    private readonly ITripRepository trips;
    private readonly IBookingImpactClient bookingImpact;
    private readonly IParcelImpactClient parcelImpact;

    public CancelTripPreviewQueryHandler(
        ITripRepository trips,
        IBookingImpactClient bookingImpact,
        IParcelImpactClient parcelImpact)
    {
        this.trips = trips;
        this.bookingImpact = bookingImpact;
        this.parcelImpact = parcelImpact;
    }

    public async Task<CancelTripPreviewResponse> Handle(
        CancelTripPreviewQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null || trip.OperatorId != request.OperatorId)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        EnsureEditable(trip.Status);

        var projection = await bookingImpact.GetTripEditImpactAsync(
            trip.Id,
            request.OperatorId,
            cancellationToken);
        var affected = projection.ActiveBookings
            .Select(booking => booking.BookingId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var refundTotalBooking = projection.ActiveBookings
            .Where(booking => booking.Status == "CONFIRMED")
            .Sum(booking => booking.TotalAmount);
        var parcels = await parcelImpact.GetTripCancellationImpactAsync(
            trip.Id,
            request.OperatorId,
            cancellationToken);
        var affectedParcelIds = parcels.AffectedParcels
            .Select(parcel => parcel.ParcelId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var refundTotalParcel = parcels.AffectedParcels.Sum(parcel => parcel.RefundAmount);

        return new CancelTripPreviewResponse(
            trip.Id,
            trip.Status.ToString(),
            affected,
            refundTotalBooking,
            affectedParcelIds,
            refundTotalParcel,
            checked(refundTotalBooking + refundTotalParcel));
    }

    internal static void EnsureEditable(TripStatus status)
    {
        if (status is not (TripStatus.SCHEDULED or TripStatus.BOARDING))
            throw new CodedConflictException("TRIP_NOT_EDITABLE", "Trip is not editable in its current status.");
    }
}
