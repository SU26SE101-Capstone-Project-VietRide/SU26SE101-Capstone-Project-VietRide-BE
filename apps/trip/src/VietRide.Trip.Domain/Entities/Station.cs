using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Canonical platform-level station. Soft-delete is tracked by <see cref="DeletedAt"/> only;
/// <see cref="IsActive"/> is a separate operational activation flag.
/// </summary>
public sealed class Station : BaseEntity<Guid>, ISoftDeletable, IActivatable
{
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? AddressStreet { get; private set; }
    public Guid? LocationId { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string Province { get; private set; } = string.Empty;
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? OperatingHours { get; private set; }
    public string? Facilities { get; private set; }
    public bool SupportsShuttle { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? MergedIntoStationId { get; private set; }

    private Station() { }

    public static Station Create(
        string name,
        string slug,
        string city,
        string province,
        string? addressStreet = null,
        decimal? latitude = null,
        decimal? longitude = null,
        string? contactPhone = null,
        string? contactEmail = null,
        string? operatingHours = null,
        string? facilities = null,
        bool supportsShuttle = false,
        Guid? locationId = null)
    {
        ValidateRequired(name, nameof(name));
        ValidateRequired(slug, nameof(slug));
        ValidateRequired(city, nameof(city));
        ValidateRequired(province, nameof(province));
        ValidateOptionalGuid(locationId, nameof(locationId));

        return new Station
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim(),
            City = city.Trim(),
            Province = province.Trim(),
            AddressStreet = NormalizeOptional(addressStreet),
            LocationId = locationId,
            Latitude = latitude,
            Longitude = longitude,
            ContactPhone = NormalizeOptional(contactPhone),
            ContactEmail = NormalizeOptional(contactEmail)?.ToLowerInvariant(),
            OperatingHours = NormalizeOptional(operatingHours),
            Facilities = NormalizeOptional(facilities),
            SupportsShuttle = supportsShuttle,
            IsActive = true,
        };
    }

    public void UpdateProfile(
        string name,
        string slug,
        string city,
        string province,
        string? addressStreet,
        Guid? locationId,
        decimal? latitude,
        decimal? longitude,
        string? contactPhone,
        string? contactEmail,
        string? operatingHours,
        string? facilities,
        bool supportsShuttle)
    {
        ValidateRequired(name, nameof(name));
        ValidateRequired(slug, nameof(slug));
        ValidateRequired(city, nameof(city));
        ValidateRequired(province, nameof(province));
        ValidateOptionalGuid(locationId, nameof(locationId));

        Name = name.Trim();
        Slug = slug.Trim();
        City = city.Trim();
        Province = province.Trim();
        AddressStreet = NormalizeOptional(addressStreet);
        LocationId = locationId;
        Latitude = latitude;
        Longitude = longitude;
        ContactPhone = NormalizeOptional(contactPhone);
        ContactEmail = NormalizeOptional(contactEmail)?.ToLowerInvariant();
        OperatingHours = NormalizeOptional(operatingHours);
        Facilities = NormalizeOptional(facilities);
        SupportsShuttle = supportsShuttle;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void MergeProfileFrom(Station duplicate)
    {
        ArgumentNullException.ThrowIfNull(duplicate);
        if (Id == duplicate.Id)
            throw new ArgumentException("A station cannot be merged into itself.", nameof(duplicate));

        if (!IsActive || DeletedAt is not null || MergedIntoStationId is not null)
            throw new InvalidOperationException("The primary station must be active, non-deleted, and canonical.");

        if (duplicate.DeletedAt is not null || duplicate.MergedIntoStationId is not null)
            throw new InvalidOperationException("The duplicate station must be non-deleted and canonical.");

        AddressStreet ??= duplicate.AddressStreet;
        LocationId ??= duplicate.LocationId;
        ContactPhone ??= duplicate.ContactPhone;
        ContactEmail ??= duplicate.ContactEmail;
        OperatingHours ??= duplicate.OperatingHours;
        Facilities ??= duplicate.Facilities;

        if (!Latitude.HasValue && !Longitude.HasValue
            && duplicate.Latitude.HasValue && duplicate.Longitude.HasValue)
        {
            Latitude = duplicate.Latitude;
            Longitude = duplicate.Longitude;
        }

        SupportsShuttle |= duplicate.SupportsShuttle;
    }

    public void MarkMergedInto(Guid primaryStationId, DateTimeOffset mergedAt)
    {
        ValidateMergeTarget(primaryStationId);
        if (DeletedAt is not null || MergedIntoStationId is not null)
            throw new InvalidOperationException("Only a non-deleted canonical station can become a merge redirect.");

        MergedIntoStationId = primaryStationId;
        SoftDelete(mergedAt);
    }

    public void FlattenMergeRedirect(Guid primaryStationId)
    {
        ValidateMergeTarget(primaryStationId);
        if (DeletedAt is null || MergedIntoStationId is null)
            throw new InvalidOperationException("Only an existing merged station redirect can be flattened.");

        MergedIntoStationId = primaryStationId;
    }

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
        IsActive = false;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }

    private static void ValidateOptionalGuid(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    private void ValidateMergeTarget(Guid primaryStationId)
    {
        if (primaryStationId == Guid.Empty)
            throw new ArgumentException("Primary station id cannot be empty.", nameof(primaryStationId));

        if (primaryStationId == Id)
            throw new ArgumentException("A station cannot redirect to itself.", nameof(primaryStationId));
    }
}
