using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Mapping between an Identity operator tenant and a canonical station.
/// OperatorId is a logical FK to Identity and is validated at the application boundary.
/// </summary>
public sealed class OperatorStation : BaseEntity<Guid>, IActivatable
{
    public Guid OperatorId { get; private set; }
    public Guid StationId { get; private set; }
    public string? DisplayNameOverride { get; private set; }
    public string? CounterLocation { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? Instructions { get; private set; }
    public bool IsActive { get; private set; } = true;

    private OperatorStation() { }

    public static OperatorStation Create(
        Guid operatorId,
        Guid stationId,
        string? displayNameOverride = null,
        string? counterLocation = null,
        string? contactPhone = null,
        string? instructions = null)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateGuid(stationId, nameof(stationId));

        return new OperatorStation
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            StationId = stationId,
            DisplayNameOverride = NormalizeOptional(displayNameOverride),
            CounterLocation = NormalizeOptional(counterLocation),
            ContactPhone = NormalizeOptional(contactPhone),
            Instructions = NormalizeOptional(instructions),
            IsActive = true,
        };
    }

    public void UpdateDetails(
        string? displayNameOverride,
        string? counterLocation,
        string? contactPhone,
        string? instructions)
    {
        DisplayNameOverride = NormalizeOptional(displayNameOverride);
        CounterLocation = NormalizeOptional(counterLocation);
        ContactPhone = NormalizeOptional(contactPhone);
        Instructions = NormalizeOptional(instructions);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
