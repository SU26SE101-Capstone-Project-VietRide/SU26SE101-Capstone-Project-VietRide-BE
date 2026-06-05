using MediatR;

namespace VietRide.Identity.Application.Features.Auth.SetInitialPassword;

public sealed record SetInitialPasswordCommand(
    string? Token,
    string Password) : IRequest<SetInitialPasswordResponseDto>;
