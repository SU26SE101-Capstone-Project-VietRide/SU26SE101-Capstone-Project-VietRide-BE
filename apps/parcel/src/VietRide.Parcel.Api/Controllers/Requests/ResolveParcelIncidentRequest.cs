namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ResolveParcelIncidentRequest(
    string? Note = null,
    string ResolutionCode = "DELIVERED_TO_CORRECT_LOCATION");
