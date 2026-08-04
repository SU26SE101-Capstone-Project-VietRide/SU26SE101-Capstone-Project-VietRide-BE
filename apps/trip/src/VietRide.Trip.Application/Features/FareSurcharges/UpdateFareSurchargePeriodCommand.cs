using MediatR;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed record UpdateFareSurchargePeriodCommand(
    Guid OperatorId,
    Guid PeriodId,
    string? Name,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? SurchargePercent,
    bool? IsActive) : IRequest<FareSurchargePeriodDto>;
