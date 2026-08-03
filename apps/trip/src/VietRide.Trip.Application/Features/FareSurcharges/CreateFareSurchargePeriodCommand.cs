using MediatR;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed record CreateFareSurchargePeriodCommand(
    Guid OperatorId,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    int SurchargePercent,
    bool IsActive) : IRequest<FareSurchargePeriodDto>;
