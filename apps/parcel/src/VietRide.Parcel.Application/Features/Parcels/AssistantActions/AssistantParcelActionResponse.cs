using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantActions;

public sealed record AssistantParcelActionResponse(
    AssistantParcelActionStateResponse ParcelState,
    ReliabilityCustodySummaryResponse? CurrentCustody,
    ReliabilityIncidentSummaryResponse? ActiveIncident,
    AssistantCreatedCustodyEventResponse? CreatedCustodyEvent,
    IReadOnlyList<string> AvailableActions,
    string? Warning);
