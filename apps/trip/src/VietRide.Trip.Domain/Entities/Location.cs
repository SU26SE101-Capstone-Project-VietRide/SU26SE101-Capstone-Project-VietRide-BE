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
    public const string WardType = "WARD";
    public const string CommuneType = "COMMUNE";
    public const string SpecialZoneType = "SPECIAL_ZONE";

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = ProvinceType;
    public Guid? ParentLocationId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    private Location() { }

    public static Location Create(
        string code,
        string name,
        string type,
        int sortOrder,
        bool isActive = true)
        => Create(code, name, type, null, sortOrder, isActive);

    public static Location Create(
        string code,
        string name,
        string type,
        Guid? parentLocationId,
        int sortOrder,
        bool isActive = true)
    {
        ValidateRequired(code, nameof(code));
        ValidateRequired(name, nameof(name));
        ValidateType(type);
        ValidateParentId(parentLocationId);
        ValidateSortOrder(sortOrder);

        return new Location
        {
            Id = Guid.NewGuid(),
            Code = NormalizeCode(code),
            Name = name.Trim(),
            Type = type.Trim().ToUpperInvariant(),
            ParentLocationId = parentLocationId,
            SortOrder = sortOrder,
            IsActive = isActive,
        };
    }

    public void UpdateDetails(string code, string name, string type, Guid? parentLocationId, int sortOrder)
    {
        ValidateRequired(code, nameof(code));
        ValidateRequired(name, nameof(name));
        ValidateType(type);
        ValidateParentId(parentLocationId);
        ValidateSortOrder(sortOrder);

        Code = NormalizeCode(code);
        Name = name.Trim();
        Type = type.Trim().ToUpperInvariant();
        ParentLocationId = parentLocationId;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public static bool IsTopLevelType(string type)
        => type is ProvinceType or MunicipalityType;

    public static bool IsLeafType(string type)
        => type is WardType or CommuneType or SpecialZoneType;

    public static bool IsSupportedType(string type)
        => IsTopLevelType(type) || IsLeafType(type);

    private static string NormalizeCode(string code) => code.Trim();

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
        if (normalized is null || !IsSupportedType(normalized))
        {
            throw new ArgumentException("Location type is not supported.", nameof(type));
        }
    }

    private static void ValidateParentId(Guid? parentLocationId)
    {
        if (parentLocationId == Guid.Empty)
        {
            throw new ArgumentException("Parent location id cannot be empty.", nameof(parentLocationId));
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
