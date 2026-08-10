using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.Locations;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class UpdateAdminStationHandler : IRequestHandler<UpdateAdminStationCommand, StationDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStationRepository _stations;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILocationRepository? _locations;

    public UpdateAdminStationHandler(
        IStationRepository stations,
        ILocationRepository locations,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _stations = stations;
        _locations = locations;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public UpdateAdminStationHandler(
        IStationRepository stations,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
        : this(stations, null!, outbox, unitOfWork, clock)
    {
    }

    public async Task<StationDto> Handle(UpdateAdminStationCommand request, CancellationToken cancellationToken)
    {
        var station = await _stations.GetForUpdateAsync(request.StationId, cancellationToken)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        if (station.MergedIntoStationId.HasValue)
        {
            throw new CodedConflictException(
                "STATION_MERGE_CONFLICT",
                "A merged Station cannot be normalized.");
        }

        if (station.DeletedAt.HasValue)
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");

        var before = StationEventSnapshot.FromStation(station);
        var name = request.Name ?? station.Name;
        var city = station.City;
        var ward = station.Ward;
        var locationId = station.LocationId;
        var effectiveLocationId = request.LocationId ?? station.LocationId;
        if (effectiveLocationId.HasValue && _locations is not null)
        {
            var location = await LocationHierarchyGuard.ResolveActiveLeafAsync(
                _locations,
                effectiveLocationId,
                null,
                nameof(request.LocationId),
                "locationCode",
                cancellationToken);
            locationId = location.Leaf.Id;
            city = location.Parent.Name;
            ward = location.Leaf.Name;
        }
        else if (request.LocationId.HasValue)
        {
            throw new InvalidOperationException("Location repository is required when changing a station location.");
        }
        var baseSlug = Slugify($"{name} {city} {ward}");
        if (baseSlug.Length == 0)
            baseSlug = $"station-{station.Id:N}";

        var slug = baseSlug;
        if (await _stations.SlugExistsAsync(slug, station.Id, cancellationToken))
        {
            var suffix = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(station.Id.ToString("D"))))[..6]
                .ToLowerInvariant();
            slug = $"{baseSlug[..Math.Min(baseSlug.Length, 93)]}-{suffix}";
        }

        station.UpdateProfile(
            name,
            slug,
            city,
            ward,
            request.AddressStreet ?? station.AddressStreet,
            locationId,
            request.Latitude ?? station.Latitude,
            request.Longitude ?? station.Longitude,
            request.ContactPhone ?? station.ContactPhone,
            request.ContactEmail ?? station.ContactEmail,
            request.OperatingHours ?? station.OperatingHours,
            request.Facilities ?? station.Facilities,
            request.SupportsShuttle ?? station.SupportsShuttle);
        if (request.IsActive == true)
            station.Activate();
        if (request.IsActive == false)
            station.Deactivate();

        _stations.Update(station);
        var integrationEvent = new StationNormalizedIntegrationEvent(
            request.ActorUserId,
            request.IpAddress,
            request.UserAgent,
            station.Id,
            before,
            StationEventSnapshot.FromStation(station),
            _clock.UtcNow);
        await _outbox.EnqueueAsync(
            integrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StationMapper.ToDto(station);
    }

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return slug[..Math.Min(slug.Length, 100)];
    }
}
