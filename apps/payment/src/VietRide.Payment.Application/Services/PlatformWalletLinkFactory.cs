using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Services;

public static class PlatformWalletLinkFactory
{
    public static IReadOnlyList<PlatformWalletTransactionLinkInput> FromPaymentContext(
        PaymentContextV1 context)
        => context.Allocations.Select(allocation => new PlatformWalletTransactionLinkInput(
            MapLinkType(allocation.ReferenceType),
            checked(allocation.GrossAmount
                - allocation.VoucherVietRideFundedAmount
                - allocation.VoucherOperatorFundedAmount),
            allocation.OperatorId,
            allocation.TripId,
            allocation.ReferenceId,
            allocation.ReferenceCode)).ToArray();

    public static IReadOnlyList<PlatformWalletTransactionLinkInput> ForRefund(
        PaymentContextV1 context,
        Guid referenceId,
        long amount)
    {
        var allocation = context.Allocations.SingleOrDefault(item => item.ReferenceId == referenceId)
            ?? throw new InvalidOperationException("Refund allocation is missing from the trusted payment context.");
        return
        [
            new PlatformWalletTransactionLinkInput(
                MapLinkType(allocation.ReferenceType),
                amount,
                allocation.OperatorId,
                allocation.TripId,
                allocation.ReferenceId,
                allocation.ReferenceCode),
        ];
    }

    private static PlatformWalletTransactionLinkType MapLinkType(string referenceType)
        => referenceType switch
        {
            "BOOKING" => PlatformWalletTransactionLinkType.BOOKING,
            "PARCEL" or "PARCEL_ADDITIONAL" => PlatformWalletTransactionLinkType.PARCEL,
            _ => throw new ArgumentOutOfRangeException(nameof(referenceType), referenceType, "Unsupported payment allocation type."),
        };
}
