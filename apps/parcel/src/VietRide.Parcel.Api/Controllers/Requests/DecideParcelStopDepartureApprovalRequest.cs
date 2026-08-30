namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record DecideParcelStopDepartureApprovalRequest(
    string Decision,
    string? Note);
