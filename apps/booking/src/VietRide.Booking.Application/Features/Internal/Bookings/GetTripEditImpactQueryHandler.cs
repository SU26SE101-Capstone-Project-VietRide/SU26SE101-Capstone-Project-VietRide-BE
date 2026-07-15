using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetTripEditImpactQueryHandler(IBookingRepository bookings)
    : IRequestHandler<GetTripEditImpactQuery, TripEditImpactDto>
{
    public Task<TripEditImpactDto> Handle(
        GetTripEditImpactQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OperatorId == Guid.Empty)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "operatorId is required and must be non-empty.");
        }

        return bookings.GetTripEditImpactAsync(
            request.TripId,
            request.OperatorId,
            cancellationToken);
    }
}
