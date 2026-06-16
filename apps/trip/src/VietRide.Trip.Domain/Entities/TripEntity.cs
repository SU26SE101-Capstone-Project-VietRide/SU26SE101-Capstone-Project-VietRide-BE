using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Trip.Domain.Entities;

public sealed class TripEntity : BaseEntity<Guid>
{
    private readonly List<TripSeat> seats = [];

    public Guid OperatorId { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid DriverUserId { get; private set; }
    public Guid? AssistantUserId { get; private set; }
    public Guid? DriverScheduleId { get; private set; }
    public DateTimeOffset DepartureDateTime { get; private set; }
    public DateTimeOffset EstimatedArrivalTime { get; private set; }
    public DateTimeOffset? ActualDepartureTime { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DisruptedAt { get; private set; }
    public string? DisruptionReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public TripStatus Status { get; private set; } = TripStatus.SCHEDULED;
    public TripSource Source { get; private set; }
    public bool HasSubstitution { get; private set; }
    public Money BaseFare { get; private set; }
    public decimal? MaxCargoWeightKg { get; private set; }
    public decimal EstimatedPassengerLuggageKg { get; private set; }
    public decimal ReservedParcelWeightKg { get; private set; }
    public decimal TotalLoadedWeightKg { get; private set; }
    public IReadOnlyCollection<TripSeat> Seats => seats.AsReadOnly();

    private TripEntity() { }

    public static TripEntity Create(
        Guid operatorId,
        Guid routeId,
        Guid vehicleId,
        Guid driverUserId,
        DateTimeOffset departureDateTime,
        DateTimeOffset estimatedArrivalTime,
        Money baseFare,
        TripSource source)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateGuid(routeId, nameof(routeId));
        ValidateGuid(vehicleId, nameof(vehicleId));
        ValidateGuid(driverUserId, nameof(driverUserId));

        if (estimatedArrivalTime <= departureDateTime)
        {
            throw new ArgumentException("Estimated arrival time must be after departure time.", nameof(estimatedArrivalTime));
        }

        return new TripEntity
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            RouteId = routeId,
            VehicleId = vehicleId,
            DriverUserId = driverUserId,
            DepartureDateTime = departureDateTime,
            EstimatedArrivalTime = estimatedArrivalTime,
            BaseFare = baseFare,
            Source = source,
            Status = TripStatus.SCHEDULED,
        };
    }

    public bool IsBookable() => Status == TripStatus.SCHEDULED;

    public void ChangeStatus(TripStatus status) => Status = status;

    public IReadOnlyList<string> FindUnavailableSeats(IEnumerable<string> seatNumbers)
    {
        var requested = seatNumbers
            .Select(NormalizeSeatNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return requested
            .Where(seatNumber => seats.FirstOrDefault(seat => seat.SeatNumber == seatNumber)?.Status != TripSeatStatus.AVAILABLE)
            .ToArray();
    }

    public void HoldSeats(IEnumerable<string> seatNumbers)
    {
        var unavailable = FindUnavailableSeats(seatNumbers);
        if (unavailable.Count > 0)
        {
            throw new InvalidOperationException("One or more seats are unavailable.");
        }

        foreach (var seatNumber in seatNumbers.Select(NormalizeSeatNumber).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            seats.First(seat => seat.SeatNumber == seatNumber).Hold();
        }
    }

    public void AddSeat(TripSeat seat)
    {
        if (seat.TripId != Id)
        {
            throw new ArgumentException("Seat belongs to a different trip.", nameof(seat));
        }

        if (seats.Any(existing => existing.SeatNumber == seat.SeatNumber))
        {
            throw new InvalidOperationException("Trip already has this seat number.");
        }

        seats.Add(seat);
    }

    private static string NormalizeSeatNumber(string seatNumber)
    {
        if (string.IsNullOrWhiteSpace(seatNumber))
        {
            throw new ArgumentException("Seat number is required.", nameof(seatNumber));
        }

        return seatNumber.Trim().ToUpperInvariant();
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
