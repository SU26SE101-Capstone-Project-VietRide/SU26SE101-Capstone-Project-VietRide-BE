using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Devices.RegisterDeviceToken;

public sealed class RegisterDeviceTokenCommandHandler : IRequestHandler<RegisterDeviceTokenCommand, RegisterDeviceTokenResponseDto>
{
    private readonly IUserDeviceRepository _devices;
    private readonly IClock _clock;

    public RegisterDeviceTokenCommandHandler(
        IUserDeviceRepository devices,
        IClock clock)
    {
        _devices = devices;
        _clock = clock;
    }

    public async Task<RegisterDeviceTokenResponseDto> Handle(
        RegisterDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        var fcmToken = request.FcmToken.Trim();
        var platform = Enum.Parse<DevicePlatform>(request.Platform, ignoreCase: true);
        var now = _clock.UtcNow;

        var device = await _devices.FindByUserAndFcmTokenAsync(request.UserId, fcmToken, cancellationToken);
        if (device is not null)
        {
            await DeactivateOtherActiveOwnerAsync(device, fcmToken, cancellationToken);
            device.Reactivate(now);
            _devices.Update(device);
            return ToResponse(device);
        }

        device = await _devices.FindByFcmTokenAsync(fcmToken, cancellationToken);
        if (device is not null && device.UserId != request.UserId)
        {
            device.ClaimBy(request.UserId, now);
            _devices.Update(device);
            return ToResponse(device);
        }

        device = UserDevice.Create(request.UserId, fcmToken, platform, now);
        await _devices.AddAsync(device, cancellationToken);

        return ToResponse(device);
    }

    private async Task DeactivateOtherActiveOwnerAsync(
        UserDevice callerDevice,
        string fcmToken,
        CancellationToken cancellationToken)
    {
        var activeOwner = await _devices.FindByFcmTokenAsync(fcmToken, cancellationToken);
        if (activeOwner is null || activeOwner.Id == callerDevice.Id || activeOwner.UserId == callerDevice.UserId)
            return;

        activeOwner.Deactivate();
        _devices.Update(activeOwner);
    }

    private static RegisterDeviceTokenResponseDto ToResponse(UserDevice device)
        => new(
            UserDeviceId: device.Id,
            FcmToken: device.FcmToken,
            Platform: device.Platform.ToString(),
            IsActive: device.IsActive);
}
