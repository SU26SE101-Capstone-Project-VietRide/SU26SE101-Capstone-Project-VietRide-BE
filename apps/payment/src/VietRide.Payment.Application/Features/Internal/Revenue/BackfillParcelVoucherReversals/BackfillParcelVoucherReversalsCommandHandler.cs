using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Revenue.BackfillParcelVoucherReversals;

public sealed class BackfillParcelVoucherReversalsCommandHandler
    : IRequestHandler<BackfillParcelVoucherReversalsCommand, BackfillParcelVoucherReversalsResult>
{
    private readonly IParcelVoucherReversalBackfillService _service;

    public BackfillParcelVoucherReversalsCommandHandler(IParcelVoucherReversalBackfillService service)
    {
        _service = service;
    }

    public Task<BackfillParcelVoucherReversalsResult> Handle(
        BackfillParcelVoucherReversalsCommand request,
        CancellationToken cancellationToken)
        => _service.ExecuteAsync(request.DryRun, cancellationToken);
}
