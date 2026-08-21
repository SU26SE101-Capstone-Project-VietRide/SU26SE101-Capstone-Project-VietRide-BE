namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record DecideParcelClaimRequest(
    string Decision,
    long? ProvenDirectLossVnd,
    string Reason);
