using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

/// <summary>
/// Calls Booking's internal booking snapshot endpoint for Parcel attach validation.
/// </summary>
public sealed class BookingServiceClient : IBookingServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<BookingServiceClient> _logger;

    public BookingServiceClient(HttpClient httpClient, ILogger<BookingServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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
}
