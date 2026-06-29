using System.Text.Json;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

public sealed class ChargePaymentCommandHandler : IRequestHandler<ChargePaymentCommand, ChargePaymentResult>
{
    private const string WalletMethod = "WALLET";
    private const string VnPayMethod = "VNPAY";

    private readonly IPaymentRepository _payments;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IVnPayClient _vnPayClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ChargePaymentCommandHandler(
        IPaymentRepository payments,
        IPlatformWalletRepository platformWallets,
        IVnPayClient vnPayClient,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _payments = payments;
        _platformWallets = platformWallets;
        _vnPayClient = vnPayClient;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ChargePaymentResult> Handle(
        ChargePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);
        var method = ParseMethod(request.Method);
        var amount = Money.FromRaw(request.Amount);

        var replay = await _payments.FindByIdempotencyKeyAsync(request.IdempotencyKey!, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            return ToResult(replay);
        }

        await _payments.AcquirePaymentReferenceLockAsync(referenceType, request.ReferenceId, cancellationToken)
            .ConfigureAwait(false);

        var existing = await _payments.FindByReferenceAsync(referenceType, request.ReferenceId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new ConflictException("PAYMENT_ALREADY_PROCESSED", "A payment already exists for the requested reference.");
        }

        return method == PaymentMethod.WALLET
            ? await ChargeWalletAsync(request, amount, cancellationToken).ConfigureAwait(false)
            : await CreateVnPayPendingRedirectAsync(request, amount, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChargePaymentResult> ChargeWalletAsync(
        ChargePaymentCommand request,
        Money amount,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);
        var now = _clock.UtcNow;
        var payment = PaymentEntity.CreatePendingRedirect(
            referenceType,
            request.ReferenceId,
            amount,
            PaymentMethod.WALLET,
            userId: request.UserId,
            idempotencyKey: request.IdempotencyKey);
        payment.MarkSucceeded(null, now);

        var (walletRef, platformRef) = MapChargeRefs(referenceType);
        await _payments.DebitWalletPaymentAsync(
                request.UserId,
                request.ReferenceId,
                amount,
                walletRef,
                cancellationToken)
            .ConfigureAwait(false);
        await _platformWallets.CreditAsync(
                amount,
                platformRef,
                request.ReferenceId,
                $"{referenceType} payment hold",
                cancellationToken)
            .ConfigureAwait(false);
        await _payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        await EnqueuePaymentSucceededAsync(payment, cancellationToken).ConfigureAwait(false);

        return ToResult(payment);
    }

    private async Task<ChargePaymentResult> CreateVnPayPendingRedirectAsync(
        ChargePaymentCommand request,
        Money amount,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);
        var vnPayTxnRef = Guid.NewGuid().ToString("D");
        var now = _clock.UtcNow;
        var redirectUrl = _vnPayClient.CreateBookingPaymentRedirectUrl(
            request.ReferenceId,
            request.UserId,
            amount,
            vnPayTxnRef,
            request.ClientIpAddress,
            now);

        var payment = PaymentEntity.CreatePendingRedirectVnPay(
            referenceType,
            request.ReferenceId,
            request.UserId,
            amount,
            vnPayTxnRef,
            request.IdempotencyKey!,
            redirectUrl);

        await _payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        return ToResult(payment);
    }

    private static (WalletTransactionRef Wallet, PlatformWalletTransactionRef Platform) MapChargeRefs(
        PaymentReferenceType referenceType) => referenceType switch
    {
        PaymentReferenceType.BOOKING => (WalletTransactionRef.BOOKING_PAYMENT, PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD),
        PaymentReferenceType.PARCEL => (WalletTransactionRef.PARCEL_PAYMENT, PlatformWalletTransactionRef.PARCEL_PAYMENT_HOLD),
        PaymentReferenceType.PARCEL_ADDITIONAL => (WalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT, PlatformWalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT_HOLD),
        _ => throw new CodedValidationException("VALIDATION_ERROR", $"Unexpected reference type {referenceType}."),
    };

    private async Task EnqueuePaymentSucceededAsync(PaymentEntity payment, CancellationToken cancellationToken)
    {
        var evt = new PaymentSucceededIntegrationEvent(
            payment.Id,
            payment.ReferenceType,
            payment.ReferenceId,
            payment.Amount.Amount);
        var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
    }

    private static ChargePaymentResult ToResult(PaymentEntity payment)
        => new(payment.Id, payment.Status.ToString(), payment.PaymentRedirectUrl);

    private static PaymentReferenceType ParseReferenceType(string value)
        => Enum.TryParse<PaymentReferenceType>(value, ignoreCase: false, out var referenceType)
            && referenceType is PaymentReferenceType.BOOKING
                or PaymentReferenceType.PARCEL
                or PaymentReferenceType.PARCEL_ADDITIONAL
            ? referenceType
            : throw new CodedValidationException("VALIDATION_ERROR",
                "Charge supports BOOKING, PARCEL, or PARCEL_ADDITIONAL references only.");

    private static PaymentMethod ParseMethod(string value)
        => Enum.TryParse<PaymentMethod>(value, ignoreCase: false, out var method)
            && method is PaymentMethod.WALLET or PaymentMethod.VNPAY
            ? method
            : throw new CodedValidationException("VALIDATION_ERROR", "method must be WALLET or VNPAY.");
}
