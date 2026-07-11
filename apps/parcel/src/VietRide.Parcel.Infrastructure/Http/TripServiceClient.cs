using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

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

    public async Task<TripCrewAuthorizationOutcome> AuthorizeAssistantForTripAsync(
        Guid tripId,
        Guid userId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"/internal/v1/trips/{tripId:D}/tracking-authorization?userId={userId:D}&role=ASSISTANT&operatorId={operatorId:D}";
            using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.TripNotFound);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new TripCrewAuthorizationOutcome(
                    TripCrewAuthorizationOutcomeKind.TransportError,
                    $"Trip service returned status {(int)response.StatusCode}.");
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<TripTrackingAuthorizationResponse>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (envelope?.Data is null)
            {
                return new TripCrewAuthorizationOutcome(
                    TripCrewAuthorizationOutcomeKind.TransportError,
                    "Trip service returned an invalid authorization response.");
            }

            return envelope.Data.Allowed && envelope.Data.Scope == "ASSISTANT"
                ? new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized)
                : new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.AuthorizeAssistantForTripAsync({TripId}) failed.", tripId);
            return new TripCrewAuthorizationOutcome(
                TripCrewAuthorizationOutcomeKind.TransportError,
                $"Trip service transport failure: {ex.Message}");
        }
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

    public async Task<RouteOwnershipOutcome> ValidateRouteOwnershipAsync(
        Guid routeId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/routes/{routeId:D}/ownership?operatorId={operatorId:D}", cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null),
                HttpStatusCode.NotFound => new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.RouteNotFound, null),
                _ => new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.TransportError,
                    $"Trip service returned status {(int)response.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.ValidateRouteOwnershipAsync({RouteId}) failed.", routeId);
            return new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.TransportError,
                $"Trip service transport failure: {ex.Message}");
        }
    }

    public async Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var weightFormatted = estimatedWeightKg.ToString(CultureInfo.InvariantCulture);
            var volumeFormatted = estimatedVolumeM3.ToString(CultureInfo.InvariantCulture);
            var query = $"/internal/v1/trips/parcel-availability?originStationId={originStationId:D}&destinationStationId={destinationStationId:D}&departureDate={departureDate:yyyy-MM-dd}&estimatedWeightKg={weightFormatted}&estimatedVolumeM3={volumeFormatted}&sizeCategory={sizeCategory}&page={page}&pageSize={pageSize}";
            using var response = await _httpClient
                .GetAsync(query, cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var paged = await response.Content
                        .ReadFromJsonAsync<PagedResult<TripAvailabilityItemDto>>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (paged is null)
                        return new ParcelTripSearchOutcome(ParcelTripSearchOutcomeKind.TransportError, null, 0, page, pageSize,
                            "Trip service returned null body on 200.");

                    var trips = paged.Items
                        .Select(item => new ParcelTripDto(
                            item.TripId,
                            item.RouteId,
                            item.OperatorId,
                            item.OperatorName,
                            item.DepartureDateTime,
                            item.AvailableCargoWeightKg,
                            item.AvailableCargoVolumeM3,
                            0))
                        .ToList();

                    return new ParcelTripSearchOutcome(
                        ParcelTripSearchOutcomeKind.Success,
                        trips,
                        (int)paged.TotalItems,
                        paged.Page,
                        paged.PageSize,
                        null);

                case HttpStatusCode.NotFound:
                case HttpStatusCode.NotImplemented:
                    return new ParcelTripSearchOutcome(ParcelTripSearchOutcomeKind.TransportError, null, 0, page, pageSize,
                        $"Trip parcel-availability endpoint returned {(int)response.StatusCode}.");

                default:
                    return new ParcelTripSearchOutcome(ParcelTripSearchOutcomeKind.TransportError, null, 0, page, pageSize,
                        $"Trip service returned status {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.SearchAvailableParcelTripsAsync failed.");
            return new ParcelTripSearchOutcome(ParcelTripSearchOutcomeKind.TransportError, null, 0, page, pageSize,
                $"Trip service transport failure: {ex.Message}");
        }
    }

    public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => SearchAvailableParcelTripsAsync(
            originStationId,
            destinationStationId,
            departureDate,
            estimatedWeightKg,
            estimatedVolumeM3: 0.0001m,
            sizeCategory,
            page,
            pageSize,
            cancellationToken);

    public Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync("reserve", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: false, cancellationToken);

    public Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => ReserveCargoAsync(tripId, parcelId, weightKg, volumeM3: 0.0001m, cancellationToken);

    public Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync("reserve", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: true, cancellationToken);

    public async Task<TripCargoOutcome> GetCargoCapacityAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/trips/{tripId:D}/cargo/capacity", cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => new TripCargoOutcome(
                    TripCargoOutcomeKind.Success,
                    null,
                    await response.Content
                        .ReadFromJsonAsync<TripCargoCapacitySnapshot>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false)),
                HttpStatusCode.NotFound => new TripCargoOutcome(TripCargoOutcomeKind.TripNotFound, null),
                _ => new TripCargoOutcome(TripCargoOutcomeKind.TransportError,
                    $"Trip cargo capacity endpoint returned status {(int)response.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.GetCargoCapacityAsync({TripId}) failed.", tripId);
            return new TripCargoOutcome(TripCargoOutcomeKind.TransportError,
                $"Trip service transport failure: {ex.Message}");
        }
    }

    public Task<TripCargoOutcome> RemeasureCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow = false,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync("remeasure", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow, cancellationToken);

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync("load", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: false, cancellationToken);

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => LoadCargoAsync(tripId, parcelId, weightKg, volumeM3: 0.0001m, cancellationToken);

    public Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync("release", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: true, cancellationToken);

    public Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => ReleaseCargoAsync(tripId, parcelId, weightKg, volumeM3: 0.0001m, cancellationToken);

    private async Task<TripCargoOutcome> SendCargoMutationAsync(
        string action,
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/internal/v1/trips/{tripId:D}/cargo/{action}")
            {
                Content = JsonContent.Create(new
                {
                    parcelId,
                    weightKg,
                    volumeM3,
                    allowCapacityOverflow,
                    idempotencyKey = $"parcel:cargo:{action}:{parcelId:D}",
                }, options: JsonOptions),
            };

            request.Headers.TryAddWithoutValidation("Idempotency-Key", $"parcel:cargo:{action}:{parcelId:D}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => new TripCargoOutcome(
                    TripCargoOutcomeKind.Success,
                    null,
                    await response.Content
                        .ReadFromJsonAsync<TripCargoCapacitySnapshot>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false)),
                HttpStatusCode.NotFound => new TripCargoOutcome(TripCargoOutcomeKind.TripNotFound, null),
                HttpStatusCode.Conflict => new TripCargoOutcome(TripCargoOutcomeKind.CapacityExceeded, "Trip cargo capacity would be exceeded."),
                _ => new TripCargoOutcome(TripCargoOutcomeKind.TransportError,
                    $"Trip cargo endpoint returned status {(int)response.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient cargo {Action} failed for parcel {ParcelId}.", action, parcelId);
            return new TripCargoOutcome(TripCargoOutcomeKind.TransportError,
                $"Trip service transport failure: {ex.Message}");
        }
    }
}
