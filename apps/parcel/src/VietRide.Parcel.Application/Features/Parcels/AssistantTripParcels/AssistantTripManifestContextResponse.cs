using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantTripManifestContextResponse(
    ReliabilityTripResponse Trip,
    AssistantOperationalLocationResponse? CurrentOperationalLocation,
    IReadOnlyList<ReliabilityTripStopResponse> OrderedStops);
