using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Parcels.Received;

public sealed record ReceivedParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    ReceivedParcelStationResponse? OriginStation,
    ReceivedParcelStationResponse? DestinationStation,
    DateTimeOffset? Eta,
    Guid SenderUserId,
    string? RecipientName,
    string SizeCategory,
    DateTimeOffset CreatedAt,
    Guid OperatorId,
    Guid TripId,
    ReliabilityOperatorResponse? Operator = null,
    ReliabilityLocationResponse? DropoffLocation = null,
    ParcelReliabilitySummaryResponse? Reliability = null);

public sealed record ReceivedParcelStationResponse(Guid Id, string Name);
