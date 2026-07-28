using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.CheckIn;

public sealed record CheckInParcelCommand(
    Guid ParcelId,
    Guid TripId,
    string ParcelCode,
    IReadOnlyCollection<string>? PhotoUrls,
    Guid AssistantUserId,
    Guid OperatorId) : IRequest<CheckInParcelResponse>;
