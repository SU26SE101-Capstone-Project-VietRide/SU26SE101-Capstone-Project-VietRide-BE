using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Locations;

internal static class LocationHierarchyGuard
{
    internal sealed record ResolvedLeaf(Location Leaf, Location Parent);

    public static void ValidateOfficialCode(string code, string type)
    {
        var expectedLength = Location.IsTopLevelType(type) ? 2 : 5;
        if (code.Length != expectedLength || code.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new ValidationException(
                "Official administrative code validation failed.",
                [new ValidationError("code", $"{type} code must contain exactly {expectedLength} digits.")]);
        }
    }

    public static async Task<ResolvedLeaf> ResolveActiveLeafAsync(
        ILocationRepository repository,
        Guid? locationId,
        string? locationCode,
        string locationIdField,
        string locationCodeField,
        CancellationToken cancellationToken)
    {
        Location? location = locationId.HasValue
            ? await repository.GetActiveByIdAsync(locationId.Value, cancellationToken)
            : await repository.GetActiveByCodeAsync(locationCode!, cancellationToken);

        if (location is null || !Location.IsLeafType(location.Type) || !location.ParentLocationId.HasValue)
        {
            var field = locationId.HasValue ? locationIdField : locationCodeField;
            throw new ValidationException(
                "Location logical FK validation failed.",
                [new ValidationError(field, "Location was not found, inactive, or not a ward/commune/special zone.")]);
        }

        var parent = await repository.GetActiveByIdAsync(location.ParentLocationId.Value, cancellationToken);
        if (parent is null || !Location.IsTopLevelType(parent.Type))
        {
            var field = locationId.HasValue ? locationIdField : locationCodeField;
            throw new ValidationException(
                "Location logical FK validation failed.",
                [new ValidationError(field, "Location parent was not found, inactive, or not top-level.")]);
        }

        return new ResolvedLeaf(location, parent);
    }

    public static async Task<Location?> ResolveParentAsync(
        ILocationRepository repository,
        string targetType,
        string? parentCode,
        Guid? existingParentId,
        CancellationToken cancellationToken)
    {
        if (Location.IsTopLevelType(targetType))
        {
            if (!string.IsNullOrWhiteSpace(parentCode))
            {
                throw new ValidationException(
                    "Location hierarchy validation failed.",
                    [new ValidationError("parentCode", "Province and municipality locations cannot have a parent.")]);
            }

            return null;
        }

        Location? parent;
        if (!string.IsNullOrWhiteSpace(parentCode))
        {
            parent = await repository.GetActiveByCodeAsync(parentCode, cancellationToken);
        }
        else if (existingParentId.HasValue)
        {
            parent = await repository.GetActiveByIdAsync(existingParentId.Value, cancellationToken);
        }
        else
        {
            parent = null;
        }

        if (parent is null || !Location.IsTopLevelType(parent.Type))
        {
            throw new ValidationException(
                "Location hierarchy validation failed.",
                [new ValidationError("parentCode", "Ward, commune, and special zone locations require an active province or municipality parent.")]);
        }

        return parent;
    }
}
