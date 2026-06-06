using MediatR;

namespace VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;

public sealed record GetActiveDeviceTokensQuery(Guid UserId) : IRequest<IReadOnlyList<GetActiveDeviceTokensResponseDto>>;
