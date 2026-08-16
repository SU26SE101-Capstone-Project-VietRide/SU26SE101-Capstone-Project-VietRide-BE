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
        foreach (var seat in outbound.Where(seat => seat.Status != TripSeatStatus.BOOKED))
        {
            seat.MarkBooked(request.Outbound.BookingId);
        }
        foreach (var seat in @return.Where(seat => seat.Status != TripSeatStatus.BOOKED))
        {
            seat.MarkBooked(request.Return.BookingId);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await ReleaseLegLockAsync(request.Outbound, cancellationToken);
        await ReleaseLegLockAsync(request.Return, cancellationToken);
        return Unit.Value;
    }

    private async Task<IReadOnlyList<TripSeat>> ValidateLegAsync(BookRoundTripSeatsLeg leg, CancellationToken ct)
    {
        _ = await trips.GetByIdAsync(leg.TripId, ct)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var numbers = Normalize(leg.PassengerSeatAssignments.Select(x => x.SeatNumber));
        var seats = tripSeats.Query().Where(x => x.TripId == leg.TripId && numbers.Contains(x.SeatNumber)).ToArray();
        if (seats.Length != numbers.Length || seats.Any(x => x.Status is not (TripSeatStatus.HELD or TripSeatStatus.BOOKED)))
            ThrowUnavailable(numbers);
        if (seats.Any(seat => seat.Status == TripSeatStatus.BOOKED && seat.BookingId != leg.BookingId))
            ThrowUnavailable(numbers);
        var owner = leg.SeatLockToken.ToString("D");
        foreach (var seat in seats.Where(seat => seat.Status == TripSeatStatus.HELD))
        {
            if (!await locks.IsOwnedByAsync(leg.TripId, seat.SeatNumber, owner, ct)) ThrowUnavailable(numbers);
        }
        return seats;
    }

    private Task ReleaseLegLockAsync(BookRoundTripSeatsLeg leg, CancellationToken cancellationToken)
        => locks.ReleaseAsync(
            leg.TripId,
            Normalize(leg.PassengerSeatAssignments.Select(assignment => assignment.SeatNumber)),
            leg.SeatLockToken.ToString("D"),
            cancellationToken);

    private static string[] Normalize(IEnumerable<string> seatNumbers) => seatNumbers
        .Select(seatNumber => seatNumber.Trim())
        .Where(seatNumber => seatNumber.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void ThrowUnavailable(IReadOnlyList<string> numbers)
        => throw new CodedConflictException("BOOKING_SEAT_UNAVAILABLE", "One or more requested seats are unavailable.",
            numbers.Select(x => new ValidationError("seatNumbers", x)).ToArray());
}
