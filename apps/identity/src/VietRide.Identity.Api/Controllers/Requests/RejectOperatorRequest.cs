namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/admin/operators/{operatorId}/reject request body.</summary>
public sealed record RejectOperatorRequest(string Reason);
