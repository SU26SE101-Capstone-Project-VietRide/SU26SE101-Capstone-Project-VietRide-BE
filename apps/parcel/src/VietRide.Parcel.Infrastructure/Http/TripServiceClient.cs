using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class TripServiceClient : ITripServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TripServiceClient> _logger;

    public TripServiceClient(HttpClient httpClient, ILogger<TripServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<TripSnapshotOutcome> GetTripParcelSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/trips/{tripId:D}", cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var snapshot = await response.Content
                        .ReadFromJsonAsync<TripParcelSnapshot>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (snapshot is null)
                        return new TripSnapshotOutcome(TripSnapshotOutcomeKind.TransportError, null,
                            "Trip service returned null body on 200.");

                    return new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, snapshot, null);

                case HttpStatusCode.NotFound:
                    return new TripSnapshotOutcome(TripSnapshotOutcomeKind.TripNotFound, null, null);

                default:
                    return new TripSnapshotOutcome(TripSnapshotOutcomeKind.TransportError, null,
                        $"Trip service returned status {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.GetTripParcelSnapshotAsync({TripId}) failed.", tripId);
            return new TripSnapshotOutcome(TripSnapshotOutcomeKind.TransportError, null,
                $"Trip service transport failure: {ex.Message}");
        }
    }
}
