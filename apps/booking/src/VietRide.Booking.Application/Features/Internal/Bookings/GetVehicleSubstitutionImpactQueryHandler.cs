using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetVehicleSubstitutionImpactQueryHandler(IBookingRepository bookings)
    : IRequestHandler<GetVehicleSubstitutionImpactQuery, VehicleSubstitutionImpactDto>
{
    public Task<VehicleSubstitutionImpactDto> Handle(
        GetVehicleSubstitutionImpactQuery request,
        CancellationToken cancellationToken)
    {
        var tripId = ParseRequiredGuid(request.TripId, "tripId");
        var operatorId = ParseRequiredGuid(request.OperatorId, "operatorId");

        return bookings.GetVehicleSubstitutionImpactAsync(
            tripId,
            operatorId,
            cancellationToken);
    }

    private static Guid ParseRequiredGuid(string? value, string fieldName)
    {
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                $"{fieldName} is required and must be a non-empty UUID.");
        }

        return parsed;
    }
}
