using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Application.Features.Devices.RemoveDeviceToken;

public sealed class RemoveDeviceTokenCommandHandler : IRequestHandler<RemoveDeviceTokenCommand, Unit>
{
    private readonly IUserDeviceRepository _devices;

    public RemoveDeviceTokenCommandHandler(IUserDeviceRepository devices)
    {
        _devices = devices;
    }

    public async Task<Unit> Handle(RemoveDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FcmToken))
            return Unit.Value;

        var fcmToken = request.FcmToken.Trim();

        var device = await _devices.FindByUserAndFcmTokenAsync(request.UserId, fcmToken, cancellationToken);
        if (device is null)
            return Unit.Value;

        device.Deactivate();
        _devices.Update(device);

        return Unit.Value;
    }
}
