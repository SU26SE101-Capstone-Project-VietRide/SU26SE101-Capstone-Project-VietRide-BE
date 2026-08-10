using System.Text.Json;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Models;

public sealed record PaymentContextV1(
    int Version,
    IReadOnlyList<PaymentAllocationV1> Allocations);

public sealed record PaymentAllocationV1(
    Guid ReferenceId,
    string ReferenceType,
    Guid OperatorId,
    Guid TripId,
    long GrossAmount,
    long VoucherVietRideFundedAmount,
    long VoucherOperatorFundedAmount,
    string? ReferenceCode = null);

public static class PaymentContextCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ValidateAndSerialize(
        PaymentContextV1? context,
        string paymentReferenceType,
        Guid paymentReferenceId,
        long paymentAmount)
    {
        if (context is null || context.Version != 1 || context.Allocations is null || context.Allocations.Count == 0)
            throw Invalid("Payment context version 1 with at least one allocation is required.");

        foreach (var allocation in context.Allocations)
        {
            if (allocation.ReferenceId == Guid.Empty
                || allocation.OperatorId == Guid.Empty
                || allocation.TripId == Guid.Empty)
            {
                throw Invalid("Payment context allocation identifiers must not be empty.");
            }

            if (allocation.ReferenceType is not ("BOOKING" or "PARCEL" or "PARCEL_ADDITIONAL"))
                throw Invalid("Payment context allocation referenceType is not supported.");

            if (allocation.ReferenceCode is not null
                && (string.IsNullOrWhiteSpace(allocation.ReferenceCode)
                    || allocation.ReferenceCode.Length > 64
                    || !string.Equals(allocation.ReferenceCode, allocation.ReferenceCode.Trim(), StringComparison.Ordinal)))
            {
                throw Invalid("Payment context allocation referenceCode must be trimmed and at most 64 characters.");
            }

            if (allocation.GrossAmount <= 0
                || allocation.VoucherVietRideFundedAmount < 0
                || allocation.VoucherOperatorFundedAmount < 0)
            {
                throw Invalid("Payment context allocation amounts are invalid.");
            }

            long totalDiscount;
            try
            {
                totalDiscount = checked(
                    allocation.VoucherVietRideFundedAmount + allocation.VoucherOperatorFundedAmount);
            }
            catch (OverflowException)
            {
                throw Invalid("Payment context allocation total is outside the supported range.");
            }
            if (totalDiscount > allocation.GrossAmount)
                throw Invalid("Payment context voucher funding exceeds gross amount.");
        }

        if (context.Allocations
            .GroupBy(x => (x.ReferenceType, x.ReferenceId))
            .Any(group => group.Count() > 1))
        {
            throw Invalid("Payment context contains duplicate allocations.");
        }

        long paidAmount;
        try
        {
            paidAmount = context.Allocations.Sum(allocation => checked(
                allocation.GrossAmount
                - allocation.VoucherVietRideFundedAmount
                - allocation.VoucherOperatorFundedAmount));
        }
        catch (OverflowException)
        {
            throw Invalid("Payment context allocation total is outside the supported range.");
        }

        if (paidAmount != paymentAmount)
            throw Invalid("Payment context allocation economics do not equal the payment amount.");

        if (paymentReferenceType == "BOOKING_GROUP")
        {
            if (context.Allocations.Count < 2 || context.Allocations.Any(x => x.ReferenceType != "BOOKING"))
                throw Invalid("BOOKING_GROUP context requires at least two BOOKING allocations.");
        }
        else if (context.Allocations.Count != 1
                 || context.Allocations[0].ReferenceType != paymentReferenceType
                 || context.Allocations[0].ReferenceId != paymentReferenceId)
        {
            throw Invalid("Payment context allocation does not match the payment reference.");
        }

        return JsonSerializer.Serialize(context, JsonOptions);
    }

    public static PaymentContextV1 DeserializeTrusted(string context)
    {
        try
        {
            return JsonSerializer.Deserialize<PaymentContextV1>(context, JsonOptions)
                ?? throw Invalid("Stored payment context is missing.");
        }
        catch (JsonException)
        {
            throw Invalid("Stored payment context is malformed.");
        }
    }

    public static bool IsMissing(string context)
        => string.IsNullOrWhiteSpace(context) || string.Equals(context, "{}", StringComparison.Ordinal);

    private static CodedValidationException Invalid(string message)
        => new("PAYMENT_CONTEXT_INVALID", message);
}
