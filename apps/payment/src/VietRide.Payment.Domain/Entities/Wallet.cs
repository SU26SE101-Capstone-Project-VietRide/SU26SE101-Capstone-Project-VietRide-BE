using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class Wallet
{
    private Wallet()
    {
    }

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
    public int RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

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
