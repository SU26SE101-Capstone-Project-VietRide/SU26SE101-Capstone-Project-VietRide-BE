using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class TripScheduleChangedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.trip.schedule_changed";
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);

    public TripScheduleChangedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        DateTimeOffset oldDeparture,
        DateTimeOffset newDeparture,
        string severity)
        : base(eventId, occurredAt.UtcDateTime)
    {
        if (severity is not ("MINOR" or "MEDIUM" or "MAJOR"))
        {
            throw new ArgumentException("Schedule change severity is not approved.", nameof(severity));
        }

        TripId = tripId;
        OperatorId = operatorId;
        OldDeparture = oldDeparture;
        NewDeparture = newDeparture;
        Severity = severity;
    }

    public Guid TripId { get; }

    public Guid OperatorId { get; }

    public DateTimeOffset OldDeparture { get; }

    public DateTimeOffset NewDeparture { get; }

    public string Severity { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;

    public static string ClassifySeverity(
        DateTimeOffset oldDeparture,
        DateTimeOffset newDeparture)
    {
        var delta = (newDeparture - oldDeparture).Duration();
        var sameIctDate = oldDeparture.ToOffset(IctOffset).Date
            == newDeparture.ToOffset(IctOffset).Date;

        if (sameIctDate && delta <= TimeSpan.FromHours(2))
        {
            return "MINOR";
        }

        if (sameIctDate && delta < TimeSpan.FromHours(6))
        {
            return "MEDIUM";
        }

        return "MAJOR";
    }
}
