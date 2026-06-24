using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;

/// <summary>
/// Consumes the canonical payment.wallet.credited event and drives the originating Payment row to
/// REFUNDED for refund credits (BSOT §8.4: referenceType ∈ BOOKING_REFUND / PARCEL_REFUND). Other
/// wallet credits (e.g. top-ups) are ignored. Idempotent: the repository transition is status-guarded.
/// </summary>
public sealed class MarkPaymentRefundedCommandHandler : IIntegrationEventHandler<WalletCreditedConsumerEvent>
{
    private const string BookingRefund = "BOOKING_REFUND";
    private const string ParcelRefund = "PARCEL_REFUND";

    private readonly IPaymentRepository _payments;
    private readonly IClock _clock;
    private readonly ILogger<MarkPaymentRefundedCommandHandler> _logger;

    public MarkPaymentRefundedCommandHandler(
        IPaymentRepository payments,
        IClock clock,
        ILogger<MarkPaymentRefundedCommandHandler> logger)
    {
        _payments = payments;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(MarkPaymentRefundedCommand request, CancellationToken cancellationToken)
    {
        if (!TryMapReferenceType(request.ReferenceType, out var referenceType))
        {
            // Not a refund credit (e.g. a top-up) — nothing to transition.
            return;
        }

        var transitioned = await _payments.TryMarkRefundedByReferenceAsync(
            referenceType,
            request.ReferenceId,
            _clock.UtcNow,
            cancellationToken);

        if (transitioned)
        {
            _logger.LogInformation(
                "Payment for {ReferenceType} {ReferenceId} marked REFUNDED from payment.wallet.credited.",
                request.ReferenceType,
                request.ReferenceId);
        }
        else
        {
            _logger.LogDebug(
                "payment.wallet.credited refund no-op for {ReferenceType} {ReferenceId}; no SUCCEEDED payment to refund.",
                request.ReferenceType,
                request.ReferenceId);
        }
    }

    public async Task HandleAsync(WalletCreditedConsumerEvent integrationEvent, CancellationToken cancellationToken)
        => await Handle(
            new MarkPaymentRefundedCommand(integrationEvent.ReferenceType, integrationEvent.ReferenceId),
            cancellationToken);

    private static bool TryMapReferenceType(string referenceType, out PaymentReferenceType mapped)
    {
        switch (referenceType)
        {
            case BookingRefund:
                mapped = PaymentReferenceType.BOOKING;
                return true;
            case ParcelRefund:
                mapped = PaymentReferenceType.PARCEL;
                return true;
            default:
                mapped = default;
                return false;
        }
    }
}
