using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Scheduled execution of a route with vehicle/crew snapshots.
/// </summary>
public sealed class Trip : BaseEntity<Guid>
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

    private Trip() { }

    public static Trip Create(
        Guid operatorId,
        Guid routeId,
        Guid vehicleId,
        Guid driverUserId,
        Guid? assistantUserId,
        Guid? driverScheduleId,
        DateTimeOffset departureDateTime,
        DateTimeOffset estimatedArrivalTime,
        TripSource source,
        Money baseFare,
        decimal? maxCargoWeightKg,
        decimal estimatedPassengerLuggageKg,
        bool hasSubstitution = false)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateGuid(routeId, nameof(routeId));
        ValidateGuid(vehicleId, nameof(vehicleId));
        ValidateGuid(driverUserId, nameof(driverUserId));
        ValidateOptionalGuid(assistantUserId, nameof(assistantUserId));
        ValidateOptionalGuid(driverScheduleId, nameof(driverScheduleId));
        ValidateArrivalAfterDeparture(departureDateTime, estimatedArrivalTime);
        ValidateOptionalNonNegative(maxCargoWeightKg, nameof(maxCargoWeightKg));
        ValidateNonNegative(estimatedPassengerLuggageKg, nameof(estimatedPassengerLuggageKg));

        return new Trip
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            RouteId = routeId,
            VehicleId = vehicleId,
            DriverUserId = driverUserId,
            AssistantUserId = assistantUserId,
            DriverScheduleId = driverScheduleId,
            DepartureDateTime = departureDateTime,
            EstimatedArrivalTime = estimatedArrivalTime,
            Status = TripStatus.SCHEDULED,
            Source = source,
            HasSubstitution = hasSubstitution,
            BaseFare = baseFare,
            MaxCargoWeightKg = maxCargoWeightKg,
            EstimatedPassengerLuggageKg = estimatedPassengerLuggageKg,
            ReservedParcelWeightKg = 0m,
            TotalLoadedWeightKg = 0m,
        };
    }

    public void MarkBoarding(DateTimeOffset actualDepartureTime)
    {
        EnsureStatus(TripStatus.SCHEDULED, nameof(MarkBoarding));
        ActualDepartureTime = actualDepartureTime;
        Status = TripStatus.BOARDING;
    }

    public void MarkInProgress()
    {
        EnsureStatus(TripStatus.BOARDING, nameof(MarkInProgress));
        Status = TripStatus.IN_PROGRESS;
    }

    public void Complete(DateTimeOffset completedAt, Guid completedByUserId)
    {
        if (Status is not (TripStatus.BOARDING or TripStatus.IN_PROGRESS))
        {
            throw new InvalidOperationException("Only boarding or in-progress trips can be completed.");
        }

        ValidateGuid(completedByUserId, nameof(completedByUserId));
        CompletedAt = completedAt;
        CompletedByUserId = completedByUserId;
        Status = TripStatus.COMPLETED;
    }

    public void Cancel(DateTimeOffset cancelledAt, Guid cancelledByUserId, string? reason)
    {
        if (Status is TripStatus.COMPLETED or TripStatus.CANCELLED)
        {
            throw new InvalidOperationException("Completed or cancelled trips cannot be cancelled.");
        }

        ValidateGuid(cancelledByUserId, nameof(cancelledByUserId));
        CancelledAt = cancelledAt;
        CancelledByUserId = cancelledByUserId;
        CancelReason = NormalizeOptionalText(reason);
        Status = TripStatus.CANCELLED;
    }

    public void Disrupt(DateTimeOffset disruptedAt, string reason)
    {
        if (Status is TripStatus.COMPLETED or TripStatus.CANCELLED)
        {
            throw new InvalidOperationException("Completed or cancelled trips cannot be disrupted.");
        }

        DisruptedAt = disruptedAt;
        DisruptionReason = ValidateRequired(reason, nameof(reason));
        Status = TripStatus.DISRUPTED;
    }

    public void MarkSubstitution(bool hasSubstitution)
    {
        HasSubstitution = hasSubstitution;
    }

    public void UpdateCargoCounters(decimal reservedParcelWeightKg, decimal totalLoadedWeightKg)
    {
        ValidateNonNegative(reservedParcelWeightKg, nameof(reservedParcelWeightKg));
        ValidateNonNegative(totalLoadedWeightKg, nameof(totalLoadedWeightKg));

        ReservedParcelWeightKg = reservedParcelWeightKg;
        TotalLoadedWeightKg = totalLoadedWeightKg;
    }

    public bool IsBookable() => Status == TripStatus.SCHEDULED;

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
        var requested = seatNumbers
            .Select(NormalizeSeatNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unavailable = FindUnavailableSeats(requested);
        if (unavailable.Count > 0)
        {
            throw new InvalidOperationException("One or more seats are unavailable.");
        }

        foreach (var seatNumber in requested)
        {
            seats.First(seat => seat.SeatNumber == seatNumber).MarkHeld();
        }
    }

    public void AddSeat(TripSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
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

    private void EnsureStatus(TripStatus expected, string operation)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Trip must be {expected} before {operation}.");
        }
    }

    private static void ValidateArrivalAfterDeparture(DateTimeOffset departure, DateTimeOffset arrival)
    {
        if (arrival <= departure)
        {
            throw new ArgumentException("Estimated arrival must be after departure.", nameof(arrival));
        }
    }

    private static string ValidateRequired(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string NormalizeSeatNumber(string seatNumber)
    {
        if (string.IsNullOrWhiteSpace(seatNumber))
        {
            throw new ArgumentException("Seat number is required.", nameof(seatNumber));
        }

        return seatNumber.Trim().ToUpperInvariant();
    }

    private static void ValidateOptionalNonNegative(decimal? value, string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }

    private static void ValidateNonNegative(decimal value, string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }

    private static void ValidateOptionalGuid(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
