using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.VehicleSubstitution;

public sealed record ApplyVehicleSubstitutionCommand(
    Guid SourceEventId,
    DateTimeOffset OccurredAt,
    Guid OperatorId,
    Guid OldTripId,
    Guid NewTripId,
    Guid NewVehicleId,
    string NewVehiclePlateNumber,
    DateTimeOffset NewTripDepartureDateTime,
    Guid ActorUserId,
    bool NotifyPassengers,
    IReadOnlyCollection<VehicleSubstitutionMapping> Mappings) : IRequest<int>;
