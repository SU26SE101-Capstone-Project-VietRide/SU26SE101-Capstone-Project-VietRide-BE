using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class UpdateAdminStationHandler(IStationRepository stations, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateAdminStationCommand, StationDto>
{
    public async Task<StationDto> Handle(UpdateAdminStationCommand request, CancellationToken cancellationToken)
    {
        var station = await stations.GetByIdAsync(request.StationId, cancellationToken)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        var name = request.Name ?? station.Name;
        var city = request.City ?? station.City;
        var province = request.Province ?? station.Province;
        var baseSlug = Slugify($"{name} {city} {province}");
        if (baseSlug.Length == 0)
        {
            baseSlug = $"station-{station.Id:N}";
        }
        var slug = baseSlug;
        if (stations.QueryNoTracking().Any(x => x.Id != station.Id && x.Slug == slug && x.DeletedAt == null))
        {
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(station.Id.ToString())))[..6].ToLowerInvariant();
            slug = $"{baseSlug[..Math.Min(baseSlug.Length, 93)]}-{suffix}";
        }

        station.UpdateProfile(name, slug, city, province, request.AddressStreet ?? station.AddressStreet,
            request.LocationId ?? station.LocationId, request.Latitude ?? station.Latitude,
            request.Longitude ?? station.Longitude, request.ContactPhone ?? station.ContactPhone,
            request.ContactEmail ?? station.ContactEmail, request.OperatingHours ?? station.OperatingHours,
            request.Facilities ?? station.Facilities, request.SupportsShuttle ?? station.SupportsShuttle);
        if (request.IsActive == true) station.Activate();
        if (request.IsActive == false) station.Deactivate();
        stations.Update(station);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return StationMapper.ToDto(station);
    }

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        return slug[..Math.Min(slug.Length, 100)];
    }
}
