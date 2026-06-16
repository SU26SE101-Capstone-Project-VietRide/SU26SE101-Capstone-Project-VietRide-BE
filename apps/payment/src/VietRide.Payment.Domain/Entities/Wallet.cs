using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// Passenger wallet. Natural primary key is UserId (logical FK to Identity user).
/// </summary>
public sealed class Wallet : IAuditable
{
    private Wallet() { }

    public Wallet(Guid userId, Money balance, string currency = "VND")
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));

        UserId = userId;
        Balance = balance;
        Currency = currency;
    }

    public Guid UserId { get; private set; }
    public Money Balance { get; private set; }
    public string Currency { get; private set; } = "VND";
    public int RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Wallet Create(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        return new Wallet(userId, Money.Zero);
    }

    public void Credit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be positive.");

        Balance += amount;
        RowVersion++;
    }

    public (Money BalanceBefore, Money BalanceAfter) Debit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Wallet debit amount must be positive.");
        if (Balance < amount)
            throw new InvalidOperationException("Wallet balance is insufficient.");

        var before = Balance;
        Balance -= amount;
        RowVersion++;
        return (before, Balance);
    }
}
