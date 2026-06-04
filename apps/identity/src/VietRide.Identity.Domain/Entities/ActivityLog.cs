using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Domain.Entities;

public sealed class ActivityLog
{
    private ActivityLog()
    {
    }

    private ActivityLog(
        Guid userId,
        ActivityLogAction action,
        string? metadata,
        string? ipAddress,
        string? userAgent)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Activity log user id is required.", nameof(userId));

        if (ipAddress is { Length: > 45 })
            throw new ArgumentException("Activity log IP address must be at most 45 characters.", nameof(ipAddress));

        if (userAgent is { Length: > 500 })
            throw new ArgumentException("Activity log user agent must be at most 500 characters.", nameof(userAgent));

        Id = Guid.NewGuid();
        UserId = userId;
        Action = action;
        Metadata = metadata;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ActivityLogAction Action { get; private set; }

    public string? Metadata { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ActivityLog Create(
        Guid userId,
        ActivityLogAction action,
        string? metadata = null,
        string? ipAddress = null,
        string? userAgent = null)
        => new(userId, action, metadata, ipAddress, userAgent);
}
