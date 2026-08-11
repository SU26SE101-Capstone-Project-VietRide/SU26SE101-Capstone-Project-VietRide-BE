using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class Incident : BaseEntity<Guid>
{
    public Guid TripId { get; private set; }
    public Guid ReportedByUserId { get; private set; }
    public IncidentCategory Category { get; private set; }
    public string? Description { get; private set; }
    public IReadOnlyCollection<string>? PhotoUrls { get; private set; }
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public DateTimeOffset ReportedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }

    private Incident() { }

    public static Incident Create(
        Guid tripId,
        Guid reportedByUserId,
        IncidentCategory category,
        string? description,
        IReadOnlyCollection<string>? photoUrls,
        decimal? latitude,
        decimal? longitude,
        DateTimeOffset reportedAt)
    {
        ValidateGuid(tripId, nameof(tripId));
        ValidateGuid(reportedByUserId, nameof(reportedByUserId));
        var normalizedDescription = NormalizeOptional(description);
        if (normalizedDescription?.Length > 500)
        {
            throw new ArgumentException("Description cannot exceed 500 characters.", nameof(description));
        }

        var normalizedPhotoUrls = NormalizePhotoUrls(photoUrls);
        ValidateCoordinates(latitude, longitude);

        return new Incident
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            ReportedByUserId = reportedByUserId,
            Category = category,
            Description = normalizedDescription,
            PhotoUrls = normalizedPhotoUrls,
            Latitude = latitude,
            Longitude = longitude,
            ReportedAt = reportedAt,
        };
    }

    public void Resolve(Guid resolvedByUserId, string resolutionNote, DateTimeOffset resolvedAt)
    {
        ValidateGuid(resolvedByUserId, nameof(resolvedByUserId));
        if (ResolvedAt.HasValue)
        {
            throw new InvalidOperationException("Incident is already resolved.");
        }

        var normalizedNote = resolutionNote?.Trim();
        if (string.IsNullOrEmpty(normalizedNote) || normalizedNote.Length > 1000)
        {
            throw new ArgumentException(
                "Resolution note is required and cannot exceed 1000 characters.",
                nameof(resolutionNote));
        }

        ResolvedAt = resolvedAt;
        ResolvedByUserId = resolvedByUserId;
        ResolutionNote = normalizedNote;
    }

    private static IReadOnlyCollection<string>? NormalizePhotoUrls(IReadOnlyCollection<string>? photoUrls)
    {
        if (photoUrls is null || photoUrls.Count == 0)
        {
            return null;
        }

        if (photoUrls.Count > 3)
        {
            throw new ArgumentException("At most three photo URLs are allowed.", nameof(photoUrls));
        }

        var normalized = photoUrls.Select(url => url?.Trim() ?? string.Empty).ToArray();
        if (normalized.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Photo URLs must be absolute HTTPS URLs.", nameof(photoUrls));
        }

        return normalized;
    }

    private static void ValidateCoordinates(decimal? latitude, decimal? longitude)
    {
        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ArgumentException("Latitude and longitude must be supplied together.");
        }

        if (latitude is < -90m or > 90m)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90.");
        }

        if (longitude is < -180m or > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be between -180 and 180.");
        }
    }

    private static string? NormalizeOptional(string? value)
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
