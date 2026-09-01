using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class TripServiceClient : ITripServiceClient, IIdempotentTripServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

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
        => await AuthorizeCrewForTripAsync(
            tripId,
            userId,
            operatorId,
            "ASSISTANT",
            cancellationToken);

    public async Task<TripCrewAuthorizationOutcome> AuthorizeCrewForTripAsync(
        Guid tripId,
        Guid userId,
        Guid operatorId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = role.Trim().ToUpperInvariant();
        if (normalizedRole is not ("DRIVER" or "ASSISTANT"))
        {
            return new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied);
        }

        try
        {
            var path = $"/internal/v1/trips/{tripId:D}/tracking-authorization?userId={userId:D}&role={normalizedRole}&operatorId={operatorId:D}";
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

            return envelope.Data.Allowed
                && string.Equals(envelope.Data.Scope, normalizedRole, StringComparison.OrdinalIgnoreCase)
                ? new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized)
                : new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.AuthorizeCrewForTripAsync({TripId}) failed.", tripId);
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

    public async Task<TripOperationalLocationOutcome> GetTripOperationalLocationAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/trips/{tripId:D}/operational-location", cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new TripOperationalLocationOutcome(
                    TripOperationalLocationOutcomeKind.TripNotFound,
                    null,
                    null);
            if (response.StatusCode != HttpStatusCode.OK)
                return new TripOperationalLocationOutcome(
                    TripOperationalLocationOutcomeKind.TransportError,
                    null,
                    $"Trip service returned status {(int)response.StatusCode}.");

            var snapshot = await response.Content
                .ReadFromJsonAsync<TripOperationalLocationSnapshot>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return snapshot is null
                ? new TripOperationalLocationOutcome(
                    TripOperationalLocationOutcomeKind.TransportError,
                    null,
                    "Trip service returned null operational location on 200.")
                : new TripOperationalLocationOutcome(
                    TripOperationalLocationOutcomeKind.Success,
                    snapshot,
                    null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.GetTripOperationalLocationAsync({TripId}) failed.", tripId);
            return new TripOperationalLocationOutcome(
                TripOperationalLocationOutcomeKind.TransportError,
                null,
                $"Trip service transport failure: {ex.Message}");
        }
    }

    public async Task<TripSummaryBatchOutcome> GetTripSummariesAsync(
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken = default)
    {
        if (tripIds.Any(tripId => tripId == Guid.Empty))
            throw new ArgumentException("Trip ids cannot contain an empty UUID.", nameof(tripIds));

        var distinctTripIds = tripIds
            .Distinct()
            .ToArray();
        if (distinctTripIds.Length == 0)
            return TripSummaryBatchOutcome.Success([]);
        if (distinctTripIds.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(tripIds), "At most 100 distinct trip ids are allowed.");

        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync(
                    "/internal/v1/trips/summaries/batch",
                    new { tripIds = distinctTripIds },
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return TripSummaryBatchOutcome.TransportFailure(
                    $"Trip summary batch returned status {(int)response.StatusCode}.");
            }

            var summaries = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<TripSummarySnapshot>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (summaries is null)
                return TripSummaryBatchOutcome.TransportFailure("Trip summary batch returned a null body.");
            if (summaries.Any(summary => summary is null))
                return TripSummaryBatchOutcome.TransportFailure("Trip summary batch returned an invalid payload.");

            var requestedIds = distinctTripIds.ToHashSet();
            var responseIds = summaries.Select(summary => summary.TripId).ToArray();
            var malformed = responseIds.Distinct().Count() != responseIds.Length
                || summaries.Any(summary =>
                    !requestedIds.Contains(summary.TripId)
                    || string.IsNullOrWhiteSpace(summary.Status)
                    || summary.DepartureAt == default
                    || summary.ArrivalEstimate == default
                    || summary.Route is null
                    || summary.Route.RouteId == Guid.Empty
                    || string.IsNullOrWhiteSpace(summary.Route.Name)
                    || string.IsNullOrWhiteSpace(summary.Route.OriginName)
                    || string.IsNullOrWhiteSpace(summary.Route.DestinationName)
                    || summary.Vehicle is null
                    || summary.Vehicle.VehicleId == Guid.Empty
                    || string.IsNullOrWhiteSpace(summary.Vehicle.LicensePlate)
                    || string.IsNullOrWhiteSpace(summary.Vehicle.Status));
            return malformed
                ? TripSummaryBatchOutcome.TransportFailure("Trip summary batch returned an invalid payload.")
                : TripSummaryBatchOutcome.Success(summaries);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.GetTripSummariesAsync failed.");
            return TripSummaryBatchOutcome.TransportFailure(
                $"Trip summary batch transport failure: {ex.Message}");
        }
    }

    public async Task<TripForwardingOptionsOutcome> GetForwardingOptionsAsync(
        Guid operatorId,
        Guid? excludedTripId,
        string pickupLocationType,
        Guid pickupLocationId,
        string targetLocationType,
        Guid targetLocationId,
        decimal weightKg,
        decimal volumeM3,
        DateTimeOffset earliestDeparture,
        int limit,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/internal/v1/trips/forwarding-options",
                new
                {
                    operatorId,
                    excludedTripId,
                    pickupLocationType,
                    pickupLocationId,
                    targetLocationType,
                    targetLocationId,
                    weightKg,
                    volumeM3,
                    earliestDeparture,
                    limit,
                },
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                return TripForwardingOptionsOutcome.Failure(
                    $"Trip forwarding option search returned status {(int)response.StatusCode}.");
            var options = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<TripForwardingOptionSnapshot>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return options is null
                ? TripForwardingOptionsOutcome.Failure("Trip forwarding option search returned a null body.")
                : TripForwardingOptionsOutcome.Success(options);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.GetForwardingOptionsAsync failed.");
            return TripForwardingOptionsOutcome.Failure($"Trip service transport failure: {ex.Message}");
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

    public async Task<RouteSearchOutcome> SearchRoutesAsync(
        Guid operatorId,
        string search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var encodedSearch = Uri.EscapeDataString(search.Trim());
            using var response = await _httpClient.GetAsync(
                $"/internal/v1/routes/search?operatorId={operatorId:D}&search={encodedSearch}",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return RouteSearchOutcome.Failure(
                    $"Trip service returned status {(int)response.StatusCode} for route search.");
            }

            var payload = await response.Content.ReadFromJsonAsync<InternalRouteSearchResponse>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return payload is null
                ? RouteSearchOutcome.Failure("Trip route search returned an empty payload.")
                : RouteSearchOutcome.Success(payload.RouteIds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripServiceClient.SearchRoutesAsync failed.");
            return RouteSearchOutcome.Failure("Trip route search transport failure.");
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
                            item.Status,
                            item.OperatorId,
                            item.OperatorName,
                            new TripStationDto(item.OriginStation.Id, item.OriginStation.Name),
                            new TripStationDto(item.DestinationStation.Id, item.DestinationStation.Name),
                            item.DepartureDateTime,
                            item.EstimatedArrivalTime,
                            item.AvailableCargoWeightKg,
                            item.AvailableCargoVolumeM3,
                            0,
                            item.DropoffPoints?.Select(point => new TripDropoffPointDto(
                                point.Type,
                                point.StationId,
                                point.StopId,
                                point.Name,
                                point.OrderIndex,
                                point.EstimatedArrivalTime)).ToArray() ?? []))
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

    public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsForRoutesAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        ParcelSizeCategory sizeCategory,
        IReadOnlyCollection<Guid> eligibleRouteIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => SearchAvailableParcelTripsForRoutesAsync(
            new ParcelTripAvailabilityFilter(originStationId, destinationStationId, null, null, null),
            departureDate,
            estimatedWeightKg,
            estimatedVolumeM3,
            sizeCategory,
            eligibleRouteIds,
            page,
            pageSize,
            cancellationToken);

    public async Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsForRoutesAsync(
        ParcelTripAvailabilityFilter filter,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        ParcelSizeCategory sizeCategory,
        IReadOnlyCollection<Guid> eligibleRouteIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/internal/v1/trips/parcel-availability/search",
                new
                {
                    originStationId = filter.OriginStationId,
                    destinationStationId = filter.DestinationStationId,
                    dropoffStopId = filter.DropoffStopId,
                    destinationProvinceCode = filter.DestinationProvinceCode,
                    destinationLocationCode = filter.DestinationLocationCode,
                    departureDate,
                    estimatedWeightKg,
                    estimatedVolumeM3,
                    sizeCategory = sizeCategory.ToString(),
                    eligibleRouteIds = eligibleRouteIds.Distinct().ToArray(),
                    page,
                    pageSize,
                },
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new ParcelTripSearchOutcome(
                    ParcelTripSearchOutcomeKind.TransportError,
                    null,
                    0,
                    page,
                    pageSize,
                    $"Trip parcel availability search returned {(int)response.StatusCode}.");
            }

            var paged = await response.Content
                .ReadFromJsonAsync<PagedResult<TripAvailabilityItemDto>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (paged is null)
            {
                return new ParcelTripSearchOutcome(
                    ParcelTripSearchOutcomeKind.TransportError,
                    null,
                    0,
                    page,
                    pageSize,
                    "Trip parcel availability search returned an invalid payload.");
            }

            var trips = paged.Items.Select(item => new ParcelTripDto(
                item.TripId,
                item.RouteId,
                item.Status,
                item.OperatorId,
                item.OperatorName,
                new TripStationDto(item.OriginStation.Id, item.OriginStation.Name),
                new TripStationDto(item.DestinationStation.Id, item.DestinationStation.Name),
                item.DepartureDateTime,
                item.EstimatedArrivalTime,
                item.AvailableCargoWeightKg,
                item.AvailableCargoVolumeM3,
                0,
                item.DropoffPoints?.Select(point => new TripDropoffPointDto(
                    point.Type,
                    point.StationId,
                    point.StopId,
                    point.Name,
                    point.OrderIndex,
                    point.EstimatedArrivalTime)).ToArray() ?? [])).ToArray();

            return new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.Success,
                trips,
                (int)paged.TotalItems,
                paged.Page,
                paged.PageSize,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trip parcel availability route-filtered search failed.");
            return new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.TransportError,
                null,
                0,
                page,
                pageSize,
                "Trip parcel availability route-filtered search transport failure.");
        }
    }

    public Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync(
            "reserve", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: false, parcelId, cancellationToken);

    public Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync(
            "reserve", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: false, idempotencyKey, cancellationToken);

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
        => SendCargoMutationAsync(
            "reserve", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: true, parcelId, cancellationToken);

    public Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync(
            "reserve", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: true, idempotencyKey, cancellationToken);

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
        => SendCargoMutationAsync(
            "remeasure", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow, parcelId, cancellationToken);

    public Task<TripCargoOutcome> RemeasureCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync(
            "remeasure", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow, idempotencyKey, cancellationToken);

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync(
            "load", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: false, parcelId, cancellationToken);

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync(
            "load", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: false, idempotencyKey, cancellationToken);

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
        => SendCargoMutationAsync(
            "release", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: true, parcelId, cancellationToken);

    public Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => SendCargoMutationAsync(
            "release", tripId, parcelId, weightKg, volumeM3, allowCapacityOverflow: true, idempotencyKey, cancellationToken);

    public Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => ReleaseCargoAsync(tripId, parcelId, weightKg, volumeM3: 0.0001m, cancellationToken);

    public async Task<TripCargoTransferOutcome> TransferCargoAsync(
        Guid sourceTripId,
        Guid parcelId,
        Guid targetTripId,
        string targetState,
        bool allowCapacityOverflow,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/internal/v1/trips/{sourceTripId:D}/cargo/transfer")
            {
                Content = JsonContent.Create(new
                {
                    parcelId,
                    targetTripId,
                    targetState,
                    allowCapacityOverflow,
                }, options: JsonOptions),
            };
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                idempotencyKey.ToString("D"));

            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var envelope = await response.Content
                    .ReadFromJsonAsync<ApiResponse<TripCargoTransferSnapshot>>(
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                var transfer = envelope?.Data;
                if (transfer is null
                    || transfer.ParcelId != parcelId
                    || transfer.SourceTripId != sourceTripId
                    || transfer.TargetTripId != targetTripId
                    || !string.Equals(
                        transfer.TargetState,
                        targetState,
                        StringComparison.Ordinal))
                {
                    return new TripCargoTransferOutcome(
                        TripCargoTransferOutcomeKind.TransportError,
                        "Trip cargo transfer returned an invalid success payload.");
                }

                return new TripCargoTransferOutcome(
                    TripCargoTransferOutcomeKind.Success,
                    Transfer: transfer);
            }

            var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound when errorCode == "PARCEL_CARGO_NOT_FOUND"
                    => new TripCargoTransferOutcome(
                        TripCargoTransferOutcomeKind.ParcelCargoNotFound,
                        "The source Trip has no active cargo ledger for this Parcel."),
                HttpStatusCode.NotFound => new TripCargoTransferOutcome(
                    TripCargoTransferOutcomeKind.TripNotFound,
                    "The source or target Trip was not found."),
                HttpStatusCode.Conflict
                    when errorCode == "TRIP_CARGO_TRANSFER_CONFLICT"
                    => new TripCargoTransferOutcome(
                    TripCargoTransferOutcomeKind.Conflict,
                    "The Trip cargo transfer lost a concurrent mutation."),
                HttpStatusCode.Conflict => new TripCargoTransferOutcome(
                    TripCargoTransferOutcomeKind.TransportError,
                    string.IsNullOrWhiteSpace(errorCode)
                        ? "Trip cargo transfer returned an unknown conflict."
                        : $"Trip cargo transfer returned unresolved error '{errorCode}'."),
                HttpStatusCode.UnprocessableEntity
                    when errorCode == "TRIP_CARGO_CAPACITY_EXCEEDED"
                    => new TripCargoTransferOutcome(
                        TripCargoTransferOutcomeKind.CapacityExceeded,
                        "The target Trip does not have enough cargo capacity."),
                _ => new TripCargoTransferOutcome(
                    TripCargoTransferOutcomeKind.TransportError,
                    $"Trip cargo transfer endpoint returned status {(int)response.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TripServiceClient.TransferCargoAsync failed for parcel {ParcelId}.",
                parcelId);
            return new TripCargoTransferOutcome(
                TripCargoTransferOutcomeKind.TransportError,
                $"Trip service transport failure: {ex.Message}");
        }
    }

    private async Task<TripCargoOutcome> SendCargoMutationAsync(
        string action,
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        Guid idempotencyKey,
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
                    idempotencyKey,
                }, options: JsonOptions),
            };

            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("D"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var conflictErrorCode = response.StatusCode == HttpStatusCode.Conflict
                ? await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false)
                : null;
            return response.StatusCode switch
            {
                HttpStatusCode.OK => new TripCargoOutcome(
                    TripCargoOutcomeKind.Success,
                    null,
                    await response.Content
                        .ReadFromJsonAsync<TripCargoCapacitySnapshot>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false)),
                HttpStatusCode.NotFound => new TripCargoOutcome(TripCargoOutcomeKind.TripNotFound, null),
                HttpStatusCode.Conflict when conflictErrorCode == "TRIP_CARGO_CAPACITY_EXCEEDED"
                    => new TripCargoOutcome(
                        TripCargoOutcomeKind.CapacityExceeded,
                        "Trip cargo capacity would be exceeded."),
                HttpStatusCode.Conflict when conflictErrorCode == "TRIP_CARGO_STATE_INVALID"
                    => new TripCargoOutcome(
                        TripCargoOutcomeKind.InvalidState,
                        "Trip cargo is not in a state that allows this operation."),
                HttpStatusCode.Conflict => new TripCargoOutcome(
                    TripCargoOutcomeKind.TransportError,
                    string.IsNullOrWhiteSpace(conflictErrorCode)
                        ? "Trip cargo endpoint returned an unknown conflict."
                        : $"Trip cargo endpoint returned unresolved error '{conflictErrorCode}'."),
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

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return envelope?.Error?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
