using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Manifest.GetTripManifest;

public sealed record GetTripManifestQuery(
    Guid TripId,
    Guid CallerUserId) : IQuery<GetTripManifestResult>;
