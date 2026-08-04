using MediatR;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed record GetFareSurchargeSettingQuery(Guid OperatorId) : IRequest<FareSurchargeSettingDto>;
