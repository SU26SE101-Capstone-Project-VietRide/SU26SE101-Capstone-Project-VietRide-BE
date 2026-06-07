namespace VietRide.Identity.Api.Controllers.Requests;

public sealed record IncrementOperatorUsageRequest(string Resource, int Delta);
