using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetPendingPassengerCountHandler(IBookingRepository bookings)
    : IRequestHandler<GetPendingPassengerCountQuery, PendingPassengerCountDto>
{
    public async Task<PendingPassengerCountDto> Handle(
        GetPendingPassengerCountQuery request,
        CancellationToken cancellationToken)
    {
        var tripId = ParseRequiredGuid(request.TripId, "tripId");
        var stopId = ParseRequiredGuid(request.StopId, "stopId");
        var operatorId = ParseRequiredGuid(request.OperatorId, "operatorId");

        var count = await bookings.GetPendingPassengerCountAsync(
            tripId,
            stopId,
            operatorId,
            cancellationToken);

        return new PendingPassengerCountDto(tripId, stopId, count);
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
