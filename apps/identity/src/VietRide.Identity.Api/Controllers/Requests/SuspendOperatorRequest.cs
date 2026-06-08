namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/admin/operators/{operatorId}/suspend request body.</summary>
public sealed record SuspendOperatorRequest(string Reason);
