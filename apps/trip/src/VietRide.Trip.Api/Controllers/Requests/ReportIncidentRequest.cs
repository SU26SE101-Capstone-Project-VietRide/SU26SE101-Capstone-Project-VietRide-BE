namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record ReportIncidentRequest(
    string Category,
    string? Description,
    IReadOnlyCollection<string>? PhotoUrls,
    decimal? Latitude,
    decimal? Longitude);
