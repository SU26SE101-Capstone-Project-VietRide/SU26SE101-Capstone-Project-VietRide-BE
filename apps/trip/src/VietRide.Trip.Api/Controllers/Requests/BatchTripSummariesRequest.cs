namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record BatchTripSummariesRequest(IReadOnlyList<Guid>? TripIds);
