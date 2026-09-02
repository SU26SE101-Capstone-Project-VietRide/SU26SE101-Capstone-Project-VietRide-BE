using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.Management;

public static class FinancialTaxonomy
{
    public static (string BusinessGroup, string CashFlowPurpose) Platform(
        PlatformWalletTransactionRef referenceType)
        => referenceType switch
        {
            PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD => ("TICKET", "CUSTOMER_FUNDS_HELD"),
            PlatformWalletTransactionRef.PARCEL_PAYMENT_HOLD
                or PlatformWalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT_HOLD
                => ("PARCEL", "CUSTOMER_FUNDS_HELD"),
            PlatformWalletTransactionRef.BOOKING_REFUND
                or PlatformWalletTransactionRef.PARCEL_REFUND
                => ("REFUND", "CUSTOMER_REFUND"),
            PlatformWalletTransactionRef.TRIP_SETTLEMENT => ("SETTLEMENT", "OPERATOR_PAYOUT"),
            PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT => ("SUBSCRIPTION", "PLATFORM_REVENUE"),
            PlatformWalletTransactionRef.PARCEL_COMPENSATION => ("COMPENSATION", "PARCEL_COMPENSATION_PAYOUT"),
            PlatformWalletTransactionRef.MANUAL_ADJUSTMENT => ("MANUAL_ADJUSTMENT", "MANUAL_ADJUSTMENT"),
            _ => throw new ArgumentOutOfRangeException(nameof(referenceType), referenceType, null),
        };

    public static (string BusinessGroup, string CashFlowPurpose) OperatorWallet(
        OperatorWalletTransactionRef referenceType)
        => referenceType switch
        {
            OperatorWalletTransactionRef.TRIP_SETTLEMENT => ("SETTLEMENT", "OPERATOR_PAYOUT_RECEIVED"),
            OperatorWalletTransactionRef.SUBSCRIPTION_PAYMENT => ("SUBSCRIPTION", "PLATFORM_SERVICE_PAYMENT"),
            OperatorWalletTransactionRef.ADJUSTMENT => ("MANUAL_ADJUSTMENT", "MANUAL_ADJUSTMENT"),
            OperatorWalletTransactionRef.PARCEL_COMPENSATION => ("COMPENSATION", "PARCEL_COMPENSATION_PAYOUT"),
            _ => throw new ArgumentOutOfRangeException(nameof(referenceType), referenceType, null),
        };

    public static string LedgerBusinessGroup(OperatorLedgerEntry item)
        => item.EntryType switch
        {
            OperatorLedgerEntryType.BOOKING_REFUND or OperatorLedgerEntryType.PARCEL_REFUND => "REFUND",
            OperatorLedgerEntryType.PARCEL_COMPENSATION => "COMPENSATION",
            OperatorLedgerEntryType.ADJUSTMENT => "MANUAL_ADJUSTMENT",
            _ when item.ReferenceType == OperatorLedgerReferenceType.PARCEL => "PARCEL",
            _ => "TICKET",
        };

    public static string OperatorEffect(OperatorLedgerEntry item)
        => item.EntryType switch
        {
            OperatorLedgerEntryType.BOOKING_REVENUE
                or OperatorLedgerEntryType.PARCEL_REVENUE
                or OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT
                => "INCREASES_ENTITLEMENT",
            OperatorLedgerEntryType.BOOKING_REFUND
                or OperatorLedgerEntryType.PARCEL_REFUND
                => "DECREASES_ENTITLEMENT",
            OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT => "AUDIT_ONLY",
            OperatorLedgerEntryType.PARCEL_COMPENSATION when item.Amount > 0 => "INCREASES_ENTITLEMENT",
            OperatorLedgerEntryType.PARCEL_COMPENSATION when item.Amount < 0 => "DECREASES_ENTITLEMENT",
            OperatorLedgerEntryType.ADJUSTMENT
                when item.AdjustmentReason == OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL
                    && item.Amount < 0
                => "DECREASES_ENTITLEMENT",
            OperatorLedgerEntryType.ADJUSTMENT when item.Amount > 0 => "INCREASES_WALLET_BALANCE",
            OperatorLedgerEntryType.ADJUSTMENT when item.Amount < 0 => "DECREASES_WALLET_BALANCE",
            _ => "AUDIT_ONLY",
        };
}
