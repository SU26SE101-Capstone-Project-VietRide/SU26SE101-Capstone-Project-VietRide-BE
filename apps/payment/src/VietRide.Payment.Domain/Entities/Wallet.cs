using System.ComponentModel.DataAnnotations;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// Passenger wallet. Natural primary key is <see cref="UserId"/> (logical FK to Identity user).
/// </summary>
public sealed class Wallet : IAuditable
{
    public Guid UserId { get; private set; }
    public Money Balance { get; private set; }
    public string Currency { get; private set; } = "VND";

    [ConcurrencyCheck]
    public int RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Wallet() { }

    public static Wallet Create(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        return new Wallet
        {
            UserId = userId,
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
            throw new InvalidOperationException("Wallet balance cannot be negative.");

        Balance -= amount;
        RowVersion++;
    }
}
