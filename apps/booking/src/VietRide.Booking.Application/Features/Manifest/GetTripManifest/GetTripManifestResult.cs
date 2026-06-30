namespace VietRide.Booking.Application.Features.Manifest.GetTripManifest;

public sealed record GetTripManifestResult(
    IReadOnlyList<GetTripManifestItem> Items);
