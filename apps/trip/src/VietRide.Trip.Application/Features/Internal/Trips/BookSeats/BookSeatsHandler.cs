using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.BookSeats;

public sealed class BookSeatsHandler : IRequestHandler<BookSeatsCommand>
{
    private readonly ISeatLockStore seatLockStore;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly IUnitOfWork unitOfWork;

    public BookSeatsHandler(
        ITripRepository tripRepository,
        ITripSeatRepository tripSeatRepository,
        ISeatLockStore seatLockStore,
        IUnitOfWork unitOfWork)
    {
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.seatLockStore = seatLockStore;
        this.unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(BookSeatsCommand request, CancellationToken cancellationToken)
    {
        _ = await tripRepository.GetByIdAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var seatNumbers = Normalize(request.PassengerSeatAssignments.Select(assignment => assignment.SeatNumber));
        var lockOwner = request.SeatLockToken.ToString("D");
        var seats = tripSeatRepository.Query()
            .Where(seat => seat.TripId == request.TripId && seatNumbers.Contains(seat.SeatNumber))
            .ToArray();

        if (seats.Length != seatNumbers.Length)
        {
            ThrowSeatUnavailable(seatNumbers);
        }

        if (seats.All(seat => seat.Status == TripSeatStatus.BOOKED))
        {
            await EnsureSeatLocksOwnedByTokenAsync(request.TripId, seats, lockOwner, seatNumbers, cancellationToken);
            return Unit.Value;
        }

        if (!seats.All(seat => seat.Status == TripSeatStatus.HELD))
        {
            ThrowSeatUnavailable(seatNumbers);
        }

        await EnsureSeatLocksOwnedByTokenAsync(request.TripId, seats, lockOwner, seatNumbers, cancellationToken);

        foreach (var seat in seats)
        {
            seat.MarkBooked();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    private async Task EnsureSeatLocksOwnedByTokenAsync(
        Guid tripId,
        IReadOnlyCollection<TripSeat> seats,
        string lockOwner,
        IReadOnlyList<string> requestedSeatNumbers,
        CancellationToken cancellationToken)
    {
        foreach (var seatNumber in requestedSeatNumbers)
        {
            var seat = seats.First(seat => string.Equals(seat.SeatNumber, seatNumber, StringComparison.OrdinalIgnoreCase));
            if (!await seatLockStore.IsOwnedByAsync(tripId, seat.SeatNumber, lockOwner, cancellationToken))
            {
                ThrowSeatUnavailable(requestedSeatNumbers);
            }
        }
    }

    private static string[] Normalize(IEnumerable<string> seatNumbers) => seatNumbers
        .Select(seatNumber => seatNumber.Trim())
        .Where(seatNumber => seatNumber.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static void ThrowSeatUnavailable(IReadOnlyList<string> seatNumbers) =>
        throw new CodedConflictException(
            "BOOKING_SEAT_UNAVAILABLE",
            "One or more requested seats are unavailable.",
            seatNumbers.Select(seatNumber => new ValidationError("seatNumbers", seatNumber)).ToArray());
}
