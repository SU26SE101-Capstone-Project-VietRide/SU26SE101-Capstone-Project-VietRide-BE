using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;

public sealed class GetActiveDeviceTokensQueryHandler : IRequestHandler<GetActiveDeviceTokensQuery, IReadOnlyList<GetActiveDeviceTokensResponseDto>>
{
    private readonly IUserDeviceRepository _userDeviceRepository;

    public GetActiveDeviceTokensQueryHandler(IUserDeviceRepository userDeviceRepository)
    {
        _userDeviceRepository = userDeviceRepository;
    }

    public async Task<IReadOnlyList<GetActiveDeviceTokensResponseDto>> Handle(
        GetActiveDeviceTokensQuery request,
        CancellationToken cancellationToken)
    {
        var devices = await _userDeviceRepository.ListActiveByUserIdAsync(request.UserId, cancellationToken);

        return devices
            .Select(device => new GetActiveDeviceTokensResponseDto(
                device.FcmToken,
                device.Platform.ToString()))
            .ToList();
    }
}
