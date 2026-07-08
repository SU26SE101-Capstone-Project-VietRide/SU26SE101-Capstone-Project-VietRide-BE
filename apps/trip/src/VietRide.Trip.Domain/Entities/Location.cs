using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Passenger-facing location catalog used for origin/destination search.
/// </summary>
public sealed class Location : BaseEntity<Guid>, IActivatable
{
    public const string ProvinceType = "PROVINCE";
    public const string MunicipalityType = "MUNICIPALITY";

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = ProvinceType;
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    private Location() { }

    public static Location Create(
        string code,
        string name,
        string type,
        int sortOrder,
        bool isActive = true)
    {
        ValidateRequired(code, nameof(code));
        ValidateRequired(name, nameof(name));
        ValidateType(type);
        ValidateSortOrder(sortOrder);

        return new Location
        {
            Id = Guid.NewGuid(),
            Code = NormalizeCode(code),
            Name = name.Trim(),
            Type = type.Trim().ToUpperInvariant(),
            SortOrder = sortOrder,
            IsActive = isActive,
        };
    }

    public void UpdateDetails(string code, string name, string type, int sortOrder)
    {
        ValidateRequired(code, nameof(code));
        ValidateRequired(name, nameof(name));
        ValidateType(type);
        ValidateSortOrder(sortOrder);

        Code = NormalizeCode(code);
        Name = name.Trim();
        Type = type.Trim().ToUpperInvariant();
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }

    private static void ValidateType(string type)
    {
        var normalized = type?.Trim().ToUpperInvariant();
        if (normalized is not (ProvinceType or MunicipalityType))
        {
            throw new ArgumentException("Location type must be PROVINCE or MUNICIPALITY.", nameof(type));
        }
    }

    private static void ValidateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), sortOrder, "Sort order cannot be negative.");
        }
    }
}
