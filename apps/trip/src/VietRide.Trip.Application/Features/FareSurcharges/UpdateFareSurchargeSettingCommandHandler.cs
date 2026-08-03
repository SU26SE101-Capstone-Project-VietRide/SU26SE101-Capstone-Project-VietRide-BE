using MediatR;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class UpdateFareSurchargeSettingCommandHandler
    : IRequestHandler<UpdateFareSurchargeSettingCommand, FareSurchargeSettingDto>
{
    private readonly IIdentityInternalClient _identity;
    private readonly IOperatorFareSurchargeSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateFareSurchargeSettingCommandHandler(
        IIdentityInternalClient identity,
        IOperatorFareSurchargeSettingRepository settings,
        IUnitOfWork unitOfWork)
    {
        _identity = identity;
        _settings = settings;
        _unitOfWork = unitOfWork;
    }

    public async Task<FareSurchargeSettingDto> Handle(
        UpdateFareSurchargeSettingCommand request,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            _identity,
            request.OperatorId,
            cancellationToken);

        var setting = await _settings.GetByOperatorIdAsync(request.OperatorId, cancellationToken);
        if (setting is null)
        {
            setting = OperatorFareSurchargeSetting.Create(request.OperatorId, request.IsEnabled);
            await _settings.AddAsync(setting, cancellationToken);
        }
        else
        {
            setting.SetEnabled(request.IsEnabled);
            _settings.Update(setting);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new FareSurchargeSettingDto(setting.IsEnabled);
    }
}
