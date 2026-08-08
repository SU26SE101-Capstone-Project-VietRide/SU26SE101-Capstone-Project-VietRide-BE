using System.Globalization;
using System.Text.Json;
using Polly.CircuitBreaker;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Reports;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class PaymentOperatorRevenueSummaryClient : IPaymentOperatorRevenueSummaryClient
{
    private const string IctTimezone = "Asia/Ho_Chi_Minh";
    private readonly HttpClient client;

    public PaymentOperatorRevenueSummaryClient(HttpClient client)
    {
        this.client = client;
    }

    public async Task<PaymentOperatorRevenueSummaryDto> GetAsync(
        Guid operatorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"internal/v1/revenue/operators/{operatorId:D}/summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        try
        {
            using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw Unavailable();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return Parse(document.RootElement, operatorId, from, to);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ParcelDependencyUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or OperationCanceledException
            or JsonException
            or IOException
            or InvalidDataException
            or OverflowException
            or BrokenCircuitException)
        {
            throw Unavailable(exception);
        }
    }

    private static PaymentOperatorRevenueSummaryDto Parse(
        JsonElement root,
        Guid expectedOperatorId,
        DateOnly expectedFrom,
        DateOnly expectedTo)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("period", out var period)
            || period.ValueKind != JsonValueKind.Object
            || !TryDate(period, "from", out var from)
            || !TryDate(period, "to", out var to)
            || from != expectedFrom
            || to != expectedTo
            || !period.TryGetProperty("timezone", out var timezone)
            || timezone.ValueKind != JsonValueKind.String
            || !string.Equals(timezone.GetString(), IctTimezone, StringComparison.Ordinal)
            || !root.TryGetProperty("operatorId", out var operatorElement)
            || operatorElement.ValueKind != JsonValueKind.String
            || !operatorElement.TryGetGuid(out var operatorId)
            || operatorId != expectedOperatorId
            || !TryInt64(root, "netRevenueVnd", out var netRevenue)
            || !TryInt64(root, "netTicketRevenueVnd", out var netTicket)
            || !TryInt64(root, "netParcelRevenueVnd", out var netParcel)
            || !TryInt64(root, "grossParcelRevenueVnd", out var grossParcel)
            || !TryInt64(root, "parcelRefundsVnd", out var parcelRefunds)
            || !root.TryGetProperty("generatedAt", out var generatedAt)
            || generatedAt.ValueKind != JsonValueKind.String
            || !generatedAt.TryGetDateTime(out var generatedAtUtc)
            || generatedAtUtc.Kind != DateTimeKind.Utc
            || grossParcel < 0
            || parcelRefunds > 0
            || netParcel != checked(grossParcel + parcelRefunds)
            || netRevenue != checked(netTicket + netParcel))
        {
            throw new InvalidDataException("Payment operator revenue summary payload is malformed.");
        }

        return new PaymentOperatorRevenueSummaryDto(grossParcel, parcelRefunds, netParcel);
    }

    private static bool TryDate(JsonElement parent, string propertyName, out DateOnly value)
    {
        value = default;
        return parent.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(
                element.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value);
    }

    private static bool TryInt64(JsonElement parent, string propertyName, out long value)
    {
        value = default;
        return parent.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value);
    }

    private static ParcelDependencyUnavailableException Unavailable(Exception? exception = null)
        => new(
            "UPSTREAM_UNAVAILABLE",
            "Payment revenue summary is temporarily unavailable.",
            exception);
}
