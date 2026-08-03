namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateFareSurchargePeriodRequest(
    string? Name,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? SurchargePercent,
    bool? IsActive);
