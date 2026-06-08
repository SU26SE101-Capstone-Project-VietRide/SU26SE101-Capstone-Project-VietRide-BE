using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class CreateOrLinkOperatorStationHandler : IRequestHandler<CreateOrLinkOperatorStationCommand, CreateOrLinkOperatorStationResponse>
{
    private const double DuplicateNearbyMeters = 100D;

    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IOperatorStationRepository operatorStationRepository;
    private readonly IStationRepository stationRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateOrLinkOperatorStationHandler(
        IIdentityInternalClient identityInternalClient,
        IOperatorStationRepository operatorStationRepository,
        IStationRepository stationRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.operatorStationRepository = operatorStationRepository;
        this.stationRepository = stationRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<CreateOrLinkOperatorStationResponse> Handle(
        CreateOrLinkOperatorStationCommand request,
        CancellationToken cancellationToken)
    {
        await ValidateOperatorCanWriteAsync(request.OperatorId, cancellationToken);

        if (request.StationId.HasValue)
        {
            return await LinkExistingStationAsync(request, request.StationId.Value, cancellationToken);
        }

        return await CreateStationAndLinkAsync(request, cancellationToken);
    }

    private async Task<CreateOrLinkOperatorStationResponse> LinkExistingStationAsync(
        CreateOrLinkOperatorStationCommand request,
        Guid stationId,
        CancellationToken cancellationToken)
    {
        var station = await stationRepository.GetByIdAsync(stationId, cancellationToken);
        if (station is null || !station.IsActive || station.DeletedAt is not null)
        {
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        }

        var existing = operatorStationRepository.Query()
            .FirstOrDefault(mapping => mapping.OperatorId == request.OperatorId && mapping.StationId == stationId);

        if (existing is not null)
        {
            return CreateOrLinkOperatorStationResponse.Linked(existing.OperatorId, existing.StationId, existing.IsActive);
        }

        var operatorStation = OperatorStation.Create(
            request.OperatorId,
            stationId,
            request.DisplayNameOverride,
            request.CounterLocation,
            request.OperatorStationContactPhone,
            request.Instructions);

        await operatorStationRepository.AddAsync(operatorStation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CreateOrLinkOperatorStationResponse.Linked(operatorStation.OperatorId, operatorStation.StationId, operatorStation.IsActive);
    }

    private async Task<CreateOrLinkOperatorStationResponse> CreateStationAndLinkAsync(
        CreateOrLinkOperatorStationCommand request,
        CancellationToken cancellationToken)
    {
        var duplicateNearby = FindNearbyStations(request.Latitude!.Value, request.Longitude!.Value);
        if (duplicateNearby.Count > 0)
        {
            return CreateOrLinkOperatorStationResponse.DuplicateNearby(duplicateNearby);
        }

        var station = Station.Create(
            request.Name!,
            CreateCollisionSafeSlug(request),
            request.City!,
            request.Province!,
            request.AddressStreet,
            request.Latitude,
            request.Longitude,
            request.StationContactPhone,
            request.ContactEmail,
            request.OperatingHours,
            request.Facilities,
            request.SupportsShuttle);

        await stationRepository.AddAsync(station, cancellationToken);

        var operatorStation = OperatorStation.Create(
            request.OperatorId,
            station.Id,
            request.DisplayNameOverride,
            request.CounterLocation,
            request.OperatorStationContactPhone,
            request.Instructions);
        await operatorStationRepository.AddAsync(operatorStation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CreateOrLinkOperatorStationResponse.Linked(operatorStation.OperatorId, operatorStation.StationId, operatorStation.IsActive);
    }

    private async Task ValidateOperatorCanWriteAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        var eligibility = await identityInternalClient.ValidateOperatorCanWriteAsync(operatorId, cancellationToken);
        if (eligibility.IsAllowed)
        {
            return;
        }

        if (eligibility.FailureStatusCode == 403)
        {
            throw new ForbiddenException("FORBIDDEN", eligibility.Message ?? "Operator is not allowed to write Trip resources.");
        }

        throw new ValidationException(
            eligibility.Message ?? "Operator logical FK validation failed.",
            [new ValidationError("operatorId", eligibility.Message ?? "Operator logical FK validation failed.")]);
    }

    private IReadOnlyList<StationSearchResult> FindNearbyStations(decimal latitude, decimal longitude)
    {
        return stationRepository.QueryNoTracking()
            .Where(station => station.IsActive && station.DeletedAt == null && station.Latitude.HasValue && station.Longitude.HasValue)
            .AsEnumerable()
            .Where(station => DistanceInMeters(latitude, longitude, station.Latitude!.Value, station.Longitude!.Value) < DuplicateNearbyMeters)
            .Select(StationMapper.ToSearchResult)
            .ToList();
    }

    private string CreateCollisionSafeSlug(CreateOrLinkOperatorStationCommand request)
    {
        var baseSlug = Slugify($"{request.Name} {request.City} {request.Province}");
        if (!SlugExists(baseSlug))
        {
            return baseSlug;
        }

        var suffix = StableSuffix($"{request.Name}|{request.AddressStreet}|{request.City}|{request.Province}|{request.Latitude}|{request.Longitude}");
        var maxBaseLength = Math.Min(baseSlug.Length, 100 - suffix.Length - 1);
        return $"{baseSlug[..maxBaseLength]}-{suffix}";
    }

    private bool SlugExists(string slug) => stationRepository.QueryNoTracking()
        .Any(station => station.Slug == slug && station.DeletedAt == null);

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        var previousWasDash = true;

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasDash = false;
                continue;
            }

            if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug[..Math.Min(slug.Length, 100)];
    }

    private static string StableSuffix(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..6].ToLowerInvariant();
    }

    private static double DistanceInMeters(decimal latitude1, decimal longitude1, decimal latitude2, decimal longitude2)
    {
        const double earthRadiusMeters = 6371000D;
        var lat1 = DegreesToRadians((double)latitude1);
        var lat2 = DegreesToRadians((double)latitude2);
        var deltaLat = DegreesToRadians((double)(latitude2 - latitude1));
        var deltaLon = DegreesToRadians((double)(longitude2 - longitude1));

        var a = Math.Sin(deltaLat / 2D) * Math.Sin(deltaLat / 2D)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2D) * Math.Sin(deltaLon / 2D);
        var c = 2D * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1D - a));
        return earthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180D;
}
