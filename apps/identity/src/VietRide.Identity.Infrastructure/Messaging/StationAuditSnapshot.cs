namespace VietRide.Identity.Infrastructure.Messaging;

public sealed record StationAuditSnapshot
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string? Ward { get; init; }

    // Kept only in the consumer payload for queued pre-migration Outbox events.
    public string? Province { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public bool SupportsShuttle { get; init; }
    public bool IsActive { get; init; }
}
