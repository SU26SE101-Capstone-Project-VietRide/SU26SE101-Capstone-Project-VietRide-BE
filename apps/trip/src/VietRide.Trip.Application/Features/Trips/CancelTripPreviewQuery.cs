using MediatR;

namespace VietRide.Trip.Application.Features.Trips;

public sealed record CancelTripPreviewQuery(Guid TripId, Guid OperatorId)
    : IRequest<CancelTripPreviewResponse>;

public sealed record CancelTripPreviewResponse(
    Guid TripId,
    string Status,
    IReadOnlyList<Guid> AffectedBookingIds,
    long RefundTotalBooking,
    IReadOnlyList<Guid> AffectedParcelIds,
    long RefundTotalParcel,
    long GrandTotal);
