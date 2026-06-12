using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public enum TripGenerationSkipReason
{
    SUBSCRIPTION_LIMIT_EXCEEDED,
    VEHICLE_CONFLICT,
    DRIVER_CONFLICT,
    OTHER,
}

/// <summary>
/// Audit log for skipped automatic trip generation attempts.
/// </summary>
public sealed class TripGenerationSkipLog : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public Guid DriverScheduleId { get; private set; }
    public DateOnly SkippedDate { get; private set; }
    public TripGenerationSkipReason Reason { get; private set; }
    public string? Message { get; private set; }

    private TripGenerationSkipLog() { }

    public static TripGenerationSkipLog Create(
        Guid operatorId,
        Guid driverScheduleId,
        DateOnly skippedDate,
        TripGenerationSkipReason reason,
        string? message)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateGuid(driverScheduleId, nameof(driverScheduleId));

        return new TripGenerationSkipLog
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            DriverScheduleId = driverScheduleId,
            SkippedDate = skippedDate,
            Reason = reason,
            Message = NormalizeOptionalText(message),
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
