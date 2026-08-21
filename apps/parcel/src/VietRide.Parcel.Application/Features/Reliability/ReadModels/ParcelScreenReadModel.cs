namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ParcelScreenReadModel(
    ReliabilityParcelSummaryResponse Parcel,
    ReliabilityOperatorResponse Operator,
    ReliabilityTripResponse Trip,
    ReliabilityTripResponse? ForwardingTrip,
    ReliabilityLocationResponse DropoffLocation,
    ParcelReliabilitySummaryResponse Reliability);
