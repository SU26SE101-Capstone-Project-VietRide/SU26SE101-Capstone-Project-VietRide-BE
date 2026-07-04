namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record RejectDeliveryRequest(Guid Token, string RejectionReason);
