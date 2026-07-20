using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Exceptions;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// HTTP client implementation for the Trip inter-service seam.
/// Implements <see cref="ITripServiceClient"/> per BSOT §3.5 line 935
/// (impl at Infrastructure/Http/, interface at Application/Abstractions/ServiceClients/).
/// Seam shapes are FROZEN (BSOT §13 row 1.8.0, API Contract lines 1065-1179).
/// </summary>
public sealed class TripServiceClient : ITripServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TripServiceClient> _logger;

    public TripServiceClient(HttpClient httpClient, ILogger<TripServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TripSnapshot?> GetTripSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
        => await GetTripSnapshotCoreAsync(
            $"/internal/v1/trips/{tripId:D}",
            cancellationToken).ConfigureAwait(false);

    public async Task<TripSnapshot> GetOperationalTripSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await GetTripSnapshotCoreAsync(
                $"/internal/v1/trips/{tripId:D}", cancellationToken).ConfigureAwait(false);
            if (snapshot is null
                || snapshot.TripId != tripId
                || string.IsNullOrWhiteSpace(snapshot.Status)
                || snapshot.Stops is null
                || snapshot.Stops.Any(stop => stop.StopId == Guid.Empty || string.IsNullOrWhiteSpace(stop.Status)))
            {
                throw new BookingUpstreamUnavailableException("Trip operational snapshot is malformed.");
            }

            return snapshot;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BookingUpstreamUnavailableException("Trip operational snapshot timed out.", exception);
        }
        catch (BookingUpstreamUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            throw new BookingUpstreamUnavailableException("Trip operational snapshot is unavailable.", exception);
        }
    }

    /// <inheritdoc/>
    public async Task<TripSnapshot?> GetTripSnapshotAsync(
        Guid tripId,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
    {
        var utcPricingAt = pricingAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var uri = $"/internal/v1/trips/{tripId:D}?pricingAt={Uri.EscapeDataString(utcPricingAt)}";
        return await GetTripSnapshotCoreAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TripSnapshot?> GetTripSnapshotCoreAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            return await response.Content
                .ReadFromJsonAsync<TripSnapshot>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<LockSeatsOutcome> LockSeatsAsync(
        Guid tripId,
        IReadOnlyList<string> seatNumbers,
        Guid holdOwnerId,
        string idempotencyKey,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new LockSeatsRequest(seatNumbers, holdOwnerId, ttlSeconds);
            using var request = BuildJsonRequest(
                HttpMethod.Post,
                $"/internal/v1/trips/{tripId:D}/lock-seats",
                body,
                idempotencyKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ReadLockSuccessAsync(response, cancellationToken)
                    .ConfigureAwait(false),

                HttpStatusCode.NotFound => new LockSeatsOutcome.TripNotFound(),

                HttpStatusCode.Conflict => await ReadLockConflictAsync(response, cancellationToken)
                    .ConfigureAwait(false),

                _ => new LockSeatsOutcome.TransportError(
                    $"Trip service returned unexpected status {(int)response.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LockSeatsOutcome.TransportError(
                $"Trip service transport failure: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<LockRoundTripSeatsOutcome> LockRoundTripSeatsAsync(
        Guid outboundTripId,
        IReadOnlyList<string> outboundSeatNumbers,
        Guid returnTripId,
        IReadOnlyList<string> returnSeatNumbers,
        Guid holdOwnerId,
        string idempotencyKey,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new LockRoundTripSeatsRequest(
                new LockRoundTripLegRequest(outboundTripId, outboundSeatNumbers),
                new LockRoundTripLegRequest(returnTripId, returnSeatNumbers),
                holdOwnerId,
                ttlSeconds);

            using var request = BuildJsonRequest(
                HttpMethod.Post,
                "/internal/v1/trips/round-trip/lock-seats",
                body,
                idempotencyKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ReadRoundTripLockSuccessAsync(response, cancellationToken)
                    .ConfigureAwait(false),

                HttpStatusCode.NotFound => new LockRoundTripSeatsOutcome.TripNotFound(Guid.Empty),

                HttpStatusCode.Conflict => await ReadRoundTripLockConflictAsync(response, cancellationToken)
                    .ConfigureAwait(false),

                _ => new LockRoundTripSeatsOutcome.TransportError(
                    $"Trip service returned unexpected status {(int)response.StatusCode} for round-trip lock."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LockRoundTripSeatsOutcome.TransportError(
                $"Trip service round-trip lock transport failure: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> BookSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        Guid bookingId,
        IReadOnlyList<PassengerSeatAssignment> passengerSeatAssignments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new BookSeatsRequest(
                seatLockToken,
                bookingId,
                passengerSeatAssignments
                    .Select(a => new BookSeatAssignmentDto(a.PassengerId, a.SeatNumber))
                    .ToList());

            using var request = BuildJsonRequest(
                HttpMethod.Post,
                $"/internal/v1/trips/{tripId:D}/book-seats",
                body,
                idempotencyKey: null);

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent)
                return true;

            if (response.StatusCode == HttpStatusCode.Conflict)
                return false; // lock expired (BOOKING_SEAT_UNAVAILABLE)

            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> BookRoundTripSeatsAsync(
        RoundTripBookSeatsLeg outbound,
        RoundTripBookSeatsLeg @return,
        CancellationToken cancellationToken = default)
    {
        var body = new BookRoundTripSeatsRequest(Map(outbound), Map(@return));
        using var request = BuildJsonRequest(HttpMethod.Post, "/internal/v1/trips/round-trip/book-seats", body, null);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent) return true;
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        response.EnsureSuccessStatusCode();
        return true;

        static BookRoundTripSeatsLegRequest Map(RoundTripBookSeatsLeg leg) => new(
            leg.TripId, leg.SeatLockToken, leg.BookingId,
            leg.PassengerSeatAssignments.Select(x => new BookSeatAssignmentDto(x.PassengerId, x.SeatNumber)).ToList());
    }

    /// <inheritdoc/>
    public async Task ReleaseSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new ReleaseSeatsRequest(seatLockToken, seatNumbers);
            using var request = BuildJsonRequest(
                HttpMethod.Post,
                $"/internal/v1/trips/{tripId:D}/release-seats",
                body,
                idempotencyKey: null);

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            // 204 = success; idempotent — ignore 404/409 (lock already released/expired)
            if (response.StatusCode is HttpStatusCode.NoContent
                or HttpStatusCode.NotFound
                or HttpStatusCode.Conflict)
            {
                return;
            }

            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Swallow transport errors — release is best-effort compensation (saga rollback path)
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReleaseSeatsAsync transport failure for trip {TripId}", tripId);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static HttpRequestMessage BuildJsonRequest<T>(
        HttpMethod method,
        string uri,
        T body,
        string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return request;
    }

    private static async Task<LockSeatsOutcome> ReadLockSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var envelope = await response.Content
            .ReadFromJsonAsync<ApiEnvelope<LockSeatsData>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (envelope?.Data is null)
            return new LockSeatsOutcome.TransportError("Trip lock-seats returned null data.");

        var result = new SeatLockResult(
            envelope.Data.SeatLockToken,
            envelope.Data.LockedSeats,
            envelope.Data.ExpiresAt);

        return new LockSeatsOutcome.Success(result);
    }

    private static async Task<LockSeatsOutcome> ReadLockConflictAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync<ApiErrorEnvelope>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            var code = envelope?.Error?.Code ?? string.Empty;

            if (string.Equals(code, "BOOKING_SEAT_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
            {
                var fields = envelope?.Error?.Fields?
                    .Where(f => f.Field == "seatNumbers")
                    .SelectMany(ExtractSeatNumbers)
                    .ToList() ?? [];

                return new LockSeatsOutcome.SeatUnavailable(fields);
            }

            return new LockSeatsOutcome.TripNotBookable(
                envelope?.Error?.Message ?? "Trip is not bookable.");
        }
        catch
        {
            return new LockSeatsOutcome.TripNotBookable("Trip is not bookable.");
        }
    }

    private static async Task<LockRoundTripSeatsOutcome> ReadRoundTripLockSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var envelope = await response.Content
            .ReadFromJsonAsync<ApiEnvelope<RoundTripLockSeatsData>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (envelope?.Data is null)
            return new LockRoundTripSeatsOutcome.TransportError("Trip round-trip lock returned null data.");

        return new LockRoundTripSeatsOutcome.Success(
            ToRoundTripSeatLockResult(envelope.Data.Outbound),
            ToRoundTripSeatLockResult(envelope.Data.Return));
    }

    private static async Task<LockRoundTripSeatsOutcome> ReadRoundTripLockConflictAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync<ApiErrorEnvelope>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            var code = envelope?.Error?.Code ?? string.Empty;
            if (string.Equals(code, "BOOKING_SEAT_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
            {
                var fields = envelope?.Error?.Fields?
                    .Where(f => f.Field == "seatNumbers")
                    .SelectMany(ExtractSeatNumbers)
                    .ToList() ?? [];

                return new LockRoundTripSeatsOutcome.SeatUnavailable(fields);
            }

            return new LockRoundTripSeatsOutcome.TripNotBookable(
                envelope?.Error?.Message ?? "One of the trips is not bookable.");
        }
        catch
        {
            return new LockRoundTripSeatsOutcome.TripNotBookable("One of the trips is not bookable.");
        }
    }

    private static RoundTripSeatLockResult ToRoundTripSeatLockResult(RoundTripLockSeatData data)
        => new(data.TripId, data.SeatLockToken, data.LockedSeats, data.ExpiresAt);

    private static IEnumerable<string> ExtractSeatNumbers(ApiErrorField field)
    {
        if (!string.IsNullOrWhiteSpace(field.Message))
        {
            yield return field.Message;
        }

        if (field.Value is not JsonElement element)
        {
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray())
            {
                var seatNumber = value.GetString();
                if (!string.IsNullOrWhiteSpace(seatNumber))
                {
                    yield return seatNumber;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var seatNumber = element.GetString();
            if (!string.IsNullOrWhiteSpace(seatNumber))
            {
                yield return seatNumber;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Internal request / response DTOs (not exposed past this file)
    // -----------------------------------------------------------------------

    private sealed record LockSeatsRequest(
        IReadOnlyList<string> SeatNumbers,
        Guid HoldOwnerId,
        int? TtlSeconds);

    private sealed record LockRoundTripSeatsRequest(
        LockRoundTripLegRequest Outbound,
        LockRoundTripLegRequest Return,
        Guid HoldOwnerId,
        int? TtlSeconds);

    private sealed record LockRoundTripLegRequest(Guid TripId, IReadOnlyList<string> SeatNumbers);

    private sealed record BookSeatsRequest(
        Guid SeatLockToken,
        Guid BookingId,
        IReadOnlyList<BookSeatAssignmentDto> PassengerSeatAssignments);

    private sealed record BookRoundTripSeatsRequest(BookRoundTripSeatsLegRequest Outbound, BookRoundTripSeatsLegRequest Return);

    private sealed record BookRoundTripSeatsLegRequest(
        Guid TripId, Guid SeatLockToken, Guid BookingId,
        IReadOnlyList<BookSeatAssignmentDto> PassengerSeatAssignments);

    private sealed record BookSeatAssignmentDto(Guid PassengerId, string SeatNumber);

    private sealed record ReleaseSeatsRequest(
        Guid SeatLockToken,
        IReadOnlyList<string> SeatNumbers);

    // ApiResponse<T> envelope shape (success path — BSOT §5.4)
    private sealed record ApiEnvelope<T>(T? Data);

    // ApiResponse error envelope shape (BSOT §5.5)
    private sealed record ApiErrorEnvelope(ApiErrorBody? Error);

    private sealed record ApiErrorBody(string Code, string Message, IReadOnlyList<ApiErrorField>? Fields);

    private sealed record ApiErrorField(string Field, string? Message, object? Value);

    // Lock-seats data payload
    private sealed record LockSeatsData(
        Guid SeatLockToken,
        IReadOnlyList<string> LockedSeats,
        DateTimeOffset ExpiresAt);

    private sealed record RoundTripLockSeatsData(
        RoundTripLockSeatData Outbound,
        RoundTripLockSeatData Return);

    private sealed record RoundTripLockSeatData(
        Guid TripId,
        Guid SeatLockToken,
        IReadOnlyList<string> LockedSeats,
        DateTimeOffset ExpiresAt);
}
