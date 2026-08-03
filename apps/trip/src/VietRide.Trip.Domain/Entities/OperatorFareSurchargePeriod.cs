using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class OperatorFareSurchargePeriod : BaseEntity<Guid>, IActivatable, ISoftDeletable
{
    public Guid OperatorId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public int SurchargePercent { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private OperatorFareSurchargePeriod() { }

    public static OperatorFareSurchargePeriod Create(
        Guid operatorId,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        int surchargePercent,
        bool isActive)
    {
        Validate(operatorId, name, startDate, endDate, surchargePercent);

        return new OperatorFareSurchargePeriod
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            Name = name.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            SurchargePercent = surchargePercent,
            IsActive = isActive,
        };
    }

    public void Update(string name, DateOnly startDate, DateOnly endDate, int surchargePercent, bool isActive)
    {
        Validate(OperatorId, name, startDate, endDate, surchargePercent);
        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        SurchargePercent = surchargePercent;
        IsActive = isActive;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
        IsActive = false;
    }

    private static void Validate(
        Guid operatorId,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        int surchargePercent)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id cannot be empty.", nameof(operatorId));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Name must contain between 1 and 120 characters.", nameof(name));
        if (endDate < startDate)
            throw new ArgumentException("End date cannot be before start date.", nameof(endDate));
        if (surchargePercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(surchargePercent), "Surcharge percent must be between 1 and 100.");
    }
}
