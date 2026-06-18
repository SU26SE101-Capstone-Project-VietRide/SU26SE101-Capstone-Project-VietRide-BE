using MediatR;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;

public interface IBatchChargePaymentDbContext
{
    Task<Wallet?> FindWalletAsync(Guid userId, CancellationToken cancellationToken);
    Task AcquirePaymentReferenceLocksAsync(IReadOnlyCollection<BatchChargePaymentCommand.Item> items, CancellationToken cancellationToken);
    Task<bool> PaymentReferenceExistsAsync(string referenceType, Guid referenceId, CancellationToken cancellationToken);
    void AddPayment(PaymentEntity payment);
    void AddWalletTransaction(WalletTransaction transaction);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class BatchChargePaymentCommandHandler
    : IRequestHandler<BatchChargePaymentCommand, BatchChargePaymentResult>
{
    private const string WalletMethod = "WALLET";
    private const string BookingReferenceType = "BOOKING";

    private readonly IBatchChargePaymentDbContext _db;
    private readonly IClock _clock;

    public BatchChargePaymentCommandHandler(IBatchChargePaymentDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<BatchChargePaymentResult> Handle(
        BatchChargePaymentCommand request,
        CancellationToken cancellationToken)
    {
        GuardSupportedRequest(request);
        await _db.AcquirePaymentReferenceLocksAsync(request.Items, cancellationToken).ConfigureAwait(false);

        var wallet = await _db.FindWalletAsync(request.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new PaymentInsufficientWalletException("Wallet was not found for the requested user.");

        var total = request.Items.Aggregate(Money.Zero, (sum, item) => sum + Money.FromRaw(item.Amount));
        if (wallet.Balance < total)
        {
            throw new PaymentInsufficientWalletException("Wallet balance is insufficient for the batch charge.");
        }

        foreach (var item in request.Items)
        {
            var exists = await _db.PaymentReferenceExistsAsync(item.ReferenceType, item.ReferenceId, cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                throw new ConflictException("PAYMENT_ALREADY_PROCESSED", "A payment already exists for one of the batch charge references.");
            }
        }

        var now = _clock.UtcNow;
        var results = new List<BatchChargePaymentResult.Item>(request.Items.Count);

        foreach (var item in request.Items)
        {
            var amount = Money.FromRaw(item.Amount);
            var (before, after) = wallet.Debit(amount);
            var payment = PaymentEntity.CreateSucceededWalletBookingCharge(item.ReferenceId, request.UserId, amount, now);
            var transaction = WalletTransaction.CreateBookingPaymentDebit(request.UserId, item.ReferenceId, amount, before, after);

            _db.AddPayment(payment);
            _db.AddWalletTransaction(transaction);

            results.Add(new BatchChargePaymentResult.Item(
                payment.Id,
                payment.ReferenceType.ToString(),
                payment.ReferenceId,
                payment.Status.ToString(),
                payment.PaymentRedirectUrl));
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new BatchChargePaymentResult(results);
    }

    private static void GuardSupportedRequest(BatchChargePaymentCommand request)
    {
        if (!string.Equals(request.Method, WalletMethod, StringComparison.Ordinal))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Batch charge supports WALLET only.");
        }

        var duplicate = request.Items
            .GroupBy(x => (x.ReferenceType, x.ReferenceId))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Duplicate payment reference in batch charge request.");
        }

        if (request.Items.Any(x => !string.Equals(x.ReferenceType, BookingReferenceType, StringComparison.Ordinal)))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Batch WALLET charge supports BOOKING references only.");
        }
    }
}
