using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.History;

public sealed record SentParcelHistoryItemDto(
    Guid ParcelId,
    string ParcelCode,
    Guid TripId,
    string Status,
    DateTimeOffset CreatedAt,
    long TotalAmount,
    string? OriginName,
    string? DestinationName,
    DateTimeOffset? DepartureDateTime,
    DateTimeOffset? EstimatedArrivalTime,
    Guid? BookingId,
    string RecipientName,
    string SizeCategory,
    string? PhotoUrl,
    string DeliveryMethod,
    ReliabilityOperatorResponse? Operator = null,
    ReliabilityLocationResponse? DropoffLocation = null,
    ParcelReliabilitySummaryResponse? Reliability = null);
