using MediatR;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed record UpdateFareSurchargeSettingCommand(
    Guid OperatorId,
    bool IsEnabled) : IRequest<FareSurchargeSettingDto>;
