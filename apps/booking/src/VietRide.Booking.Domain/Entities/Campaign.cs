using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

public sealed class Campaign : BaseEntity<Guid>, ISoftDeletable, IActivatable
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? OwnerOperatorId { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public ICollection<CampaignVoucher> CampaignVouchers { get; private set; } = [];

    private Campaign() { }

    public static Campaign Create(
        string name,
        string? description,
        Guid? ownerOperatorId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        Guid createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Campaign name cannot be empty.", nameof(name));
        if (validUntil <= validFrom)
            throw new ArgumentException("validUntil must be after validFrom.", nameof(validUntil));

        return new Campaign
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            OwnerOperatorId = ownerOperatorId,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            IsActive = true,
            CreatedByUserId = createdByUserId,
        };
    }

    public void Update(string name, string? description, DateTimeOffset validFrom, DateTimeOffset validUntil, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Campaign name cannot be empty.", nameof(name));
        if (validUntil <= validFrom)
            throw new ArgumentException("validUntil must be after validFrom.", nameof(validUntil));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        IsActive = isActive;
    }

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        if (DeletedAt.HasValue)
            return;
        DeletedAt = deletedAt;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
