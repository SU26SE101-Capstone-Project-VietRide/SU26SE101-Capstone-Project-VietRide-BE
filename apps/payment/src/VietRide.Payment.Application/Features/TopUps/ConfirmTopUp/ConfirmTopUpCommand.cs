using MediatR;

namespace VietRide.Payment.Application.Features.TopUps.ConfirmTopUp;

public sealed record ConfirmTopUpCommand(IReadOnlyDictionary<string, string> Parameters)
    : IRequest<ConfirmTopUpResult>;
