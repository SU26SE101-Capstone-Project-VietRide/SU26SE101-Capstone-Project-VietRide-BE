using MediatR;

namespace VietRide.Payment.Application.Features.Payments.GetVnPayReturnStatus;

public sealed record GetVnPayReturnStatusQuery(IReadOnlyDictionary<string, string> Parameters)
    : IRequest<VnPayReturnStatusResponse>;
