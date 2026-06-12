using System.ComponentModel.DataAnnotations;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// VietRide singleton clearing/holding wallet. Not a bank account.
/// </summary>
public sealed class PlatformWallet : IAuditable
{
    public Guid Id { get; private set; }
    public Money Balance { get; private set; }
    public string Currency { get; private set; } = "VND";

    [ConcurrencyCheck]
    public int RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private PlatformWallet() { }

    public static PlatformWallet Create()
    {
        return new PlatformWallet
        {
            Id = Guid.NewGuid(),
            Balance = Money.Zero,
            Currency = "VND",
        };
    }

    public void Credit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be positive.");

        Balance += amount;
        RowVersion++;
    }

    public void Debit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be positive.");

        if (Balance < amount)
            throw new InvalidOperationException("Platform wallet balance cannot be negative.");

        Balance -= amount;
        RowVersion++;
    }
}
