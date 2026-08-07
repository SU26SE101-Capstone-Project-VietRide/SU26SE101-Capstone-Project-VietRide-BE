using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Revenue.BackfillParcelVoucherReversals;

public sealed record BackfillParcelVoucherReversalsCommand(bool DryRun = true)
    : IRequest<BackfillParcelVoucherReversalsResult>;
