using MediatR;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class GetFareSurchargeSettingQueryHandler
    : IRequestHandler<GetFareSurchargeSettingQuery, FareSurchargeSettingDto>
{
    private readonly IOperatorFareSurchargeSettingRepository _settings;

    public GetFareSurchargeSettingQueryHandler(IOperatorFareSurchargeSettingRepository settings)
    {
        _settings = settings;
    }

    public async Task<FareSurchargeSettingDto> Handle(
        GetFareSurchargeSettingQuery request,
        CancellationToken cancellationToken)
    {
        var setting = await _settings.GetByOperatorIdAsync(request.OperatorId, cancellationToken);
        return new FareSurchargeSettingDto(setting?.IsEnabled ?? false);
    }
}
