using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Parcel.Infrastructure.Http;

/// <summary>
/// Calls Booking's internal booking snapshot endpoint for Parcel attach validation.
/// </summary>
public sealed class BookingServiceClient : IBookingServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

    private readonly HttpClient _httpClient;
    private readonly ILogger<BookingServiceClient> _logger;

    public BookingServiceClient(HttpClient httpClient, ILogger<BookingServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BookingHistoryOutcome> GetPassengerHistoryAsync(
        Guid userId,
        string? status,
        string? from,
        string? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"userId={userId:D}",
            $"page={page}",
            $"pageSize={pageSize}",
        };
        if (status is not null)
            query.Add($"status={Uri.EscapeDataString(status)}");
        if (from is not null)
            query.Add($"from={Uri.EscapeDataString(from)}");
        if (to is not null)
            query.Add($"to={Uri.EscapeDataString(to)}");

        try
        {
            using var response = await _httpClient.GetAsync(
                $"/internal/v1/bookings/history?{string.Join('&', query)}",
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new BookingHistoryOutcome(
                    false,
                    null,
                    $"Booking service returned status {(int)response.StatusCode}.");
            }

            var payload = await ReadDataAsync<BookingHistoryPage>(response, cancellationToken)
                .ConfigureAwait(false);
            return payload is null
                ? new BookingHistoryOutcome(false, null, "Booking service returned an empty history response.")
                : new BookingHistoryOutcome(true, payload, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "BookingServiceClient.GetPassengerHistoryAsync({UserId}) failed.", userId);
            return new BookingHistoryOutcome(false, null, "Booking service transport failure.");
        }
    }

    public async Task<BookingLookupOutcome> GetBookingSnapshotAsync(
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/bookings/{bookingId:D}", cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var snapshot = await DeserializeSnapshotAsync(response, cancellationToken)
                        .ConfigureAwait(false);

                    if (snapshot is null)
                        return new BookingLookupOutcome(BookingLookupOutcomeKind.TransportError, null,
                            "Booking service returned null body on 200.");

                    return new BookingLookupOutcome(BookingLookupOutcomeKind.Success, snapshot, null);

                case HttpStatusCode.NotFound:
                    return new BookingLookupOutcome(BookingLookupOutcomeKind.BookingNotFound, null, null);

                default:
                    return new BookingLookupOutcome(BookingLookupOutcomeKind.TransportError, null,
                        $"Booking service returned status {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BookingServiceClient.GetBookingSnapshotAsync({BookingId}) failed.", bookingId);
            return new BookingLookupOutcome(BookingLookupOutcomeKind.TransportError, null,
                $"Booking service transport failure: {ex.Message}");
        }
    }

    public async Task<VoucherValidationOutcome> ValidateVoucherAsync(
        string voucherCode,
        Guid operatorId,
        Guid routeId,
        Guid userId,
        long orderAmount,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/internal/v1/vouchers/validate",
                new
                {
                    voucherCode,
                    operatorId,
                    routeId,
                    userId,
                    orderAmount,
                    service = "PARCEL",
                    paymentMethod,
                },
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var payload = await ReadDataAsync<InternalValidateVoucherResponse>(response, cancellationToken)
                    .ConfigureAwait(false);
                return payload is null
                    ? new VoucherValidationOutcome(VoucherValidationOutcomeKind.TransportError, null, 0, "Booking returned empty voucher validation response.")
                    : new VoucherValidationOutcome(VoucherValidationOutcomeKind.Success, payload.VoucherId, payload.DiscountAmount, null);
            }

            if ((int)response.StatusCode is >= 400 and < 500)
                return new VoucherValidationOutcome(VoucherValidationOutcomeKind.Invalid, null, 0, $"Booking rejected voucher with status {(int)response.StatusCode}.");

            return new VoucherValidationOutcome(VoucherValidationOutcomeKind.TransportError, null, 0, $"Booking returned status {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BookingServiceClient.ValidateVoucherAsync({VoucherCode}) failed.", voucherCode);
            return new VoucherValidationOutcome(VoucherValidationOutcomeKind.TransportError, null, 0, ex.Message);
        }
    }

    public async Task<VoucherUsageOutcome> RecordVoucherUsageAsync(
        Guid voucherId,
        Guid userId,
        Guid parcelId,
        long discountAmount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/vouchers/usages")
            {
                Content = JsonContent.Create(new
                {
                    voucherId,
                    userId,
                    referenceType = "PARCEL",
                    referenceId = parcelId,
                    discountAmount,
                }),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", parcelId.ToString("D"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var payload = await ReadDataAsync<InternalRecordVoucherUsageResponse>(response, cancellationToken)
                    .ConfigureAwait(false);
                return payload is null
                    ? new VoucherUsageOutcome(VoucherUsageOutcomeKind.TransportError, null, "Booking returned empty usage response.")
                    : new VoucherUsageOutcome(VoucherUsageOutcomeKind.Success, payload.UsageId, null);
            }

            if ((int)response.StatusCode is >= 400 and < 500)
                return new VoucherUsageOutcome(VoucherUsageOutcomeKind.Invalid, null, $"Booking rejected voucher usage with status {(int)response.StatusCode}.");

            return new VoucherUsageOutcome(VoucherUsageOutcomeKind.TransportError, null, $"Booking returned status {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BookingServiceClient.RecordVoucherUsageAsync({ParcelId}) failed.", parcelId);
            return new VoucherUsageOutcome(VoucherUsageOutcomeKind.TransportError, null, ex.Message);
        }
    }

    public async Task DeleteVoucherUsageByReferenceAsync(
        Guid parcelId,
        Guid voucherUsageId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/internal/v1/vouchers/usages/by-reference?referenceType=PARCEL&referenceId={parcelId:D}");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", voucherUsageId.ToString("D"));
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AvailableVoucherDto>> GetAvailableParcelVouchersAsync(
        Guid userId,
        Guid operatorId,
        Guid routeId,
        string? paymentMethod,
        long? orderAmount,
        CancellationToken cancellationToken = default)
    {
        var url = $"/internal/v1/vouchers/available?userId={userId:D}&service=PARCEL&operatorId={operatorId:D}&routeId={routeId:D}&orderAmount={orderAmount ?? 0}";
        if (!string.IsNullOrWhiteSpace(paymentMethod))
            url += $"&paymentMethod={Uri.EscapeDataString(paymentMethod)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            return [];

        var payload = await ReadDataAsync<IReadOnlyList<AvailableVoucherDto>>(response, cancellationToken)
            .ConfigureAwait(false);
        return payload ?? [];
    }

    private static async Task<BookingSnapshot?> DeserializeSnapshotAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        var payload = root.TryGetProperty("data", out var data)
            ? data
            : root;

        return payload.Deserialize<BookingSnapshot>(JsonOptions);
    }

    private static async Task<T?> ReadDataAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var payload = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
            ? data
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var value)
                ? value
                : root;
        return payload.Deserialize<T>(JsonOptions);
    }

    private sealed record InternalValidateVoucherResponse(Guid VoucherId, long DiscountAmount);

    private sealed record InternalRecordVoucherUsageResponse(Guid UsageId);
}
