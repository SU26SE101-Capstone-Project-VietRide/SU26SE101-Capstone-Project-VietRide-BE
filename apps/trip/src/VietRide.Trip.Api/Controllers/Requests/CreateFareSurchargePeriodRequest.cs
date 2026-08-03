namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateFareSurchargePeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    int SurchargePercent,
    bool? IsActive);
