namespace VietRide.Identity.Api.Controllers.Requests;
public sealed record QuotaAllocationRequest(string Resource, Guid ResourceId, string? PeriodKey);
