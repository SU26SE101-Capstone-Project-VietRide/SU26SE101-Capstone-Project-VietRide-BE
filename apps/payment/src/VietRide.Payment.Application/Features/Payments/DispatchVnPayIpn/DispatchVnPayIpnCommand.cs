using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Payment.Application.Features.Payments.DispatchVnPayIpn;

[SkipTransaction]
public sealed record DispatchVnPayIpnCommand(IReadOnlyDictionary<string, string> Parameters)
    : IRequest<DispatchVnPayIpnResult>;
