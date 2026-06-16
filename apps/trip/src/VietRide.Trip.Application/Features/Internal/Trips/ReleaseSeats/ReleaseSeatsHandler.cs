using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.ReleaseSeats;

public sealed class ReleaseSeatsHandler : IRequestHandler<ReleaseSeatsCommand>
{
    private readonly ILogger<ReleaseSeatsHandler> logger;
    private readonly ISeatLockStore seatLockStore;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly IUnitOfWork unitOfWork;

    public ReleaseSeatsHandler(
        ITripSeatRepository tripSeatRepository,
        ISeatLockStore seatLockStore,
        IUnitOfWork unitOfWork,
        ILogger<ReleaseSeatsHandler> logger)
    {
        this.tripSeatRepository = tripSeatRepository;
        this.seatLockStore = seatLockStore;
        this.unitOfWork = unitOfWork;
        this.logger = logger;
    }

    public async Task<Unit> Handle(ReleaseSeatsCommand request, CancellationToken cancellationToken)
    {
        var seatNumbers = request.SeatNumbers.Select(seatNumber => seatNumber.Trim()).ToArray();
        var lockOwner = request.SeatLockToken.ToString("D");
        var seats = tripSeatRepository.Query()
            .Where(seat => seat.TripId == request.TripId && seatNumbers.Contains(seat.SeatNumber))
            .ToArray();

        var ownedSeats = new List<TripSeat>();
        foreach (var seat in seats.Where(seat => seat.Status == TripSeatStatus.HELD))
        {
            if (await seatLockStore.IsOwnedByAsync(request.TripId, seat.SeatNumber, lockOwner, cancellationToken))
            {
                ownedSeats.Add(seat);
                seat.Release();
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        try
        {
            await seatLockStore.ReleaseAsync(request.TripId, ownedSeats.Select(seat => seat.SeatNumber).ToArray(), lockOwner, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Redis seat lock release failed after DB release for trip {TripId}.",
                request.TripId);
        }

        return Unit.Value;
    }
}
