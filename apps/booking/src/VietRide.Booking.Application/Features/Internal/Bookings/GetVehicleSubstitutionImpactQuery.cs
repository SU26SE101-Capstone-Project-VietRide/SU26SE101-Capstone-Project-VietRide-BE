using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record GetVehicleSubstitutionImpactQuery(
    string TripId,
    string? OperatorId) : IQuery<VehicleSubstitutionImpactDto>;
