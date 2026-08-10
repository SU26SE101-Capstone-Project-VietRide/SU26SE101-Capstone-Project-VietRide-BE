using System.Text.Json;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Models;

public sealed record SubscriptionPaymentContextV1(
    int Version,
    Guid OperatorSubscriptionId,
    Guid PlanId,
    string PlanName,
    string BillingPeriod,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    SubscriptionBuyerSnapshotV1 BuyerSnapshot);

public sealed record SubscriptionBuyerSnapshotV1(
    string Name,
    string BusinessRegistrationNumber,
    string TaxCode,
    string ContactEmail,
    string ContactPhone,
    string? AddressStreet,
    string? AddressWard,
    string? AddressProvince);

public static class SubscriptionPaymentContextCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ValidateAndSerialize(
        SubscriptionPaymentContextV1? context,
        Guid operatorSubscriptionId)
    {
        if (context is null || context.Version != 1)
            throw Invalid("Subscription payment context version 1 is required.");
        if (context.OperatorSubscriptionId == Guid.Empty
            || context.OperatorSubscriptionId != operatorSubscriptionId
            || context.PlanId == Guid.Empty)
        {
            throw Invalid("Subscription payment context identifiers are invalid.");
        }

        if (context.BillingPeriod is not ("MONTHLY" or "YEARLY")
            || string.IsNullOrWhiteSpace(context.PlanName)
            || context.PeriodTo != (context.BillingPeriod == "MONTHLY"
                ? context.PeriodFrom.AddMonths(1)
                : context.PeriodFrom.AddYears(1)))
        {
            throw Invalid("Subscription payment context billing period is invalid.");
        }

        var buyer = context.BuyerSnapshot;
        if (buyer is null
            || string.IsNullOrWhiteSpace(buyer.Name)
            || string.IsNullOrWhiteSpace(buyer.BusinessRegistrationNumber)
            || string.IsNullOrWhiteSpace(buyer.TaxCode)
            || string.IsNullOrWhiteSpace(buyer.ContactEmail)
            || string.IsNullOrWhiteSpace(buyer.ContactPhone))
        {
            throw Invalid("Subscription buyer snapshot is incomplete.");
        }

        return JsonSerializer.Serialize(context, JsonOptions);
    }

    public static SubscriptionPaymentContextV1 DeserializeTrusted(string context)
    {
        try
        {
            return JsonSerializer.Deserialize<SubscriptionPaymentContextV1>(context, JsonOptions)
                ?? throw Invalid("Stored subscription payment context is missing.");
        }
        catch (JsonException)
        {
            throw Invalid("Stored subscription payment context is malformed.");
        }
    }

    private static CodedValidationException Invalid(string message)
        => new("PAYMENT_CONTEXT_INVALID", message);
}
