namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record AssignParcelIncidentRequest(Guid AssigneeUserId);

public sealed record RecordParcelSearchResultRequest(
    Guid TaskId,
    bool Found,
    string Result,
    IReadOnlyCollection<string>? EvidenceReferences);
