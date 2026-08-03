using MediatR;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed record DeleteFareSurchargePeriodCommand(Guid OperatorId, Guid PeriodId) : IRequest;
