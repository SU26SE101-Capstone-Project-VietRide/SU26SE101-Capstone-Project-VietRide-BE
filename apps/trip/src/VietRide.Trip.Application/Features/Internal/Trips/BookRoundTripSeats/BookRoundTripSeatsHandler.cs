using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.BookRoundTripSeats;

public sealed class BookRoundTripSeatsHandler(
    ITripRepository trips,
    ITripSeatRepository tripSeats,
    ISeatLockStore locks,
    IUnitOfWork unitOfWork) : IRequestHandler<BookRoundTripSeatsCommand>
{
    public async Task<Unit> Handle(BookRoundTripSeatsCommand request, CancellationToken cancellationToken)
    {
        var outbound = await ValidateLegAsync(request.Outbound, cancellationToken);
        var @return = await ValidateLegAsync(request.Return, cancellationToken);
        foreach (var seat in outbound.Concat(@return).Where(x => x.Status != TripSeatStatus.BOOKED)) seat.MarkBooked();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    private async Task<IReadOnlyList<TripSeat>> ValidateLegAsync(BookRoundTripSeatsLeg leg, CancellationToken ct)
    {
        _ = await trips.GetByIdAsync(leg.TripId, ct)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var numbers = leg.PassengerSeatAssignments.Select(x => x.SeatNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var seats = tripSeats.Query().Where(x => x.TripId == leg.TripId && numbers.Contains(x.SeatNumber)).ToArray();
        if (seats.Length != numbers.Length || seats.Any(x => x.Status is not (TripSeatStatus.HELD or TripSeatStatus.BOOKED)))
            ThrowUnavailable(numbers);
        var owner = leg.SeatLockToken.ToString("D");
        foreach (var number in numbers)
        {
            var seat = seats.First(x => string.Equals(x.SeatNumber, number, StringComparison.OrdinalIgnoreCase));
            if (!await locks.IsOwnedByAsync(leg.TripId, seat.SeatNumber, owner, ct)) ThrowUnavailable(numbers);
        }
        return seats;
    }

    private static void ThrowUnavailable(IReadOnlyList<string> numbers)
        => throw new CodedConflictException("BOOKING_SEAT_UNAVAILABLE", "One or more requested seats are unavailable.",
            numbers.Select(x => new ValidationError("seatNumbers", x)).ToArray());
}
