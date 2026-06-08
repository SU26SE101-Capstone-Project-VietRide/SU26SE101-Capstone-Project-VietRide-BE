using System.Text.Json;
using FluentValidation;

namespace VietRide.Identity.Application.Features.Operators;

public sealed class OperatorProfilePolicyValidator
{
    public static JsonElement? DefaultCancellationPolicy()
    {
        return null;
    }

    public static JsonElement DefaultParcelNoShowPolicy()
    {
        return ToJsonElement(new Dictionary<string, int>
        {
            ["noShowFeePercent"] = 0,
            ["additionalPaymentTimeoutMinutes"] = 30,
        }, defaultValue: null);
    }

    public static JsonElement DefaultLuggagePolicy()
    {
        return ToJsonElement(new Dictionary<string, int>
        {
            ["defaultLuggageKgPerSeat"] = 10,
        }, defaultValue: null);
    }

    public static JsonElement? ToNullableJsonElement(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return ToJsonElement(value, defaultValue: null);
    }

    public static JsonElement ToJsonElement(object? value, JsonElement? defaultValue)
    {
        if (value is null)
        {
            return defaultValue?.Clone() ?? JsonDocument.Parse("null").RootElement.Clone();
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.Clone();
        }

        if (value is JsonDocument jsonDocument)
        {
            return jsonDocument.RootElement.Clone();
        }

        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        return ToJsonElement(value, defaultValue: null);
    }

    public static JsonElement? NormalizeCancellationPolicy(JsonElement? policy)
    {
        if (policy is null)
        {
            return null;
        }

        var value = policy.Value;
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException("cancellationPolicy must be an array.");
        }

        var rules = new List<Dictionary<string, int>>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("hoursBeforeDeparture", out var hours)
                || !item.TryGetProperty("feePercent", out var feePercent)
                || hours.ValueKind != JsonValueKind.Number
                || feePercent.ValueKind != JsonValueKind.Number
                || !hours.TryGetInt32(out var hoursBeforeDeparture)
                || !feePercent.TryGetInt32(out var feePercentValue)
                || hoursBeforeDeparture < 0
                || feePercentValue < 0
                || feePercentValue > 100)
            {
                throw new ValidationException("cancellationPolicy must contain valid hoursBeforeDeparture and feePercent values.");
            }

            rules.Add(new Dictionary<string, int>
            {
                ["hoursBeforeDeparture"] = hoursBeforeDeparture,
                ["feePercent"] = feePercentValue,
            });
        }

        return ToJsonElement(rules.OrderBy(rule => rule["hoursBeforeDeparture"]).ToArray(), defaultValue: null);
    }

    public static JsonElement NormalizeParcelNoShowPolicy(JsonElement? policy)
    {
        if (policy is null)
        {
            return DefaultParcelNoShowPolicy();
        }

        var value = policy.Value;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("noShowFeePercent", out var noShowFeePercent)
            || !value.TryGetProperty("additionalPaymentTimeoutMinutes", out var timeoutMinutes)
            || noShowFeePercent.ValueKind != JsonValueKind.Number
            || timeoutMinutes.ValueKind != JsonValueKind.Number
            || !noShowFeePercent.TryGetInt32(out var feePercent)
            || !timeoutMinutes.TryGetInt32(out var timeout)
            || feePercent < 0
            || feePercent > 100
            || timeout < 0)
        {
            throw new ValidationException("parcelNoShowPolicy must contain valid noShowFeePercent and additionalPaymentTimeoutMinutes values.");
        }

        return ToJsonElement(new Dictionary<string, int>
        {
            ["noShowFeePercent"] = feePercent,
            ["additionalPaymentTimeoutMinutes"] = timeout,
        }, defaultValue: null);
    }

    public static JsonElement NormalizeLuggagePolicy(JsonElement? policy)
    {
        if (policy is null)
        {
            return DefaultLuggagePolicy();
        }

        var value = policy.Value;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("defaultLuggageKgPerSeat", out var defaultLuggageKgPerSeat)
            || defaultLuggageKgPerSeat.ValueKind != JsonValueKind.Number
            || !defaultLuggageKgPerSeat.TryGetInt32(out var luggageKg)
            || luggageKg < 0)
        {
            throw new ValidationException("luggagePolicy must contain a valid defaultLuggageKgPerSeat value.");
        }

        return ToJsonElement(new Dictionary<string, int>
        {
            ["defaultLuggageKgPerSeat"] = luggageKg,
        }, defaultValue: null);
    }

    private static JsonElement ToJsonElement<T>(T value, JsonElement? defaultValue)
    {
        if (value is null)
        {
            return (defaultValue ?? JsonDocument.Parse("null").RootElement).Clone();
        }

        return JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

}
