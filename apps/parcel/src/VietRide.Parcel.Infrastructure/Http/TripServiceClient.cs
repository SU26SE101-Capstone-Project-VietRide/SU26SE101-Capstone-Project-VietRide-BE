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
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var weightFormatted = estimatedWeightKg.ToString(CultureInfo.InvariantCulture);
            var query = $"/internal/v1/trips/parcel-availability?originStationId={originStationId:D}&destinationStationId={destinationStationId:D}&departureDate={departureDate:yyyy-MM-dd}&estimatedWeightKg={weightFormatted}&sizeCategory={sizeCategory}&page={page}&pageSize={pageSize}";
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
}
