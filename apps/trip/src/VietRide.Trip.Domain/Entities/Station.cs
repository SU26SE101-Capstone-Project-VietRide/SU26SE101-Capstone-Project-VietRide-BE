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
}
