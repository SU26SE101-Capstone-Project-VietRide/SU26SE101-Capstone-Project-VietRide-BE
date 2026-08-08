namespace VietRide.Payment.Application.Features.Internal.Revenue.BackfillParcelVoucherReversals;

public interface IParcelVoucherReversalBackfillService
{
    Task<BackfillParcelVoucherReversalsResult> ExecuteAsync(
        bool dryRun,
        CancellationToken cancellationToken);
}
