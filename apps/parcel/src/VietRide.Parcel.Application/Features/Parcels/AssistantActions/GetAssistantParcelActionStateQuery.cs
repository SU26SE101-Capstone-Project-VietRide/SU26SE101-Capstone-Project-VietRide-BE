using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantActions;

public sealed record GetAssistantParcelActionStateQuery(
    Guid ParcelId,
    Guid ActorUserId,
    Guid OperatorId,
    bool IncludeLatestCustodyEvent,
    string? Warning = null) : IRequest<AssistantParcelActionResponse>;
