namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record DecideParcelClaimAppealRequest(
    string Decision,
    long? RevisedProvenDirectLossVnd,
    string Reason);
