namespace VietRide.Booking.Domain.ValueObjects;

public sealed record BookingPointSnapshot
{
    public const string StationType = "STATION";
    public const string StopType = "STOP";

    public BookingPointSnapshot(
        string type,
        Guid id,
        string? displayName,
        string? address,
        DateTimeOffset? plannedAt)
    {
        if (type is not StationType and not StopType)
            throw new ArgumentException("Booking point type must be STATION or STOP.", nameof(type));
        if (id == Guid.Empty)
            throw new ArgumentException("Booking point id is required.", nameof(id));

        Type = type;
        Id = id;
        DisplayName = Normalize(displayName);
        Address = Normalize(address);
        PlannedAt = plannedAt;
    }

    public string Type { get; }
    public Guid Id { get; }
    public string? DisplayName { get; }
    public string? Address { get; }
    public DateTimeOffset? PlannedAt { get; }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
