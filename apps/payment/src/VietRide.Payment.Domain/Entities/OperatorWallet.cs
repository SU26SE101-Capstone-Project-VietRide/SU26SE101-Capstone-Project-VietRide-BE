using System.ComponentModel.DataAnnotations;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class OperatorWallet : IAuditable
{
    private OperatorWallet() { }

    public Guid OperatorId { get; private set; }
    public Money Balance { get; private set; }
    public string Currency { get; private set; } = "VND";

    [ConcurrencyCheck]
    public int RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static OperatorWallet Create(Guid operatorId)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(operatorId));

        return new OperatorWallet
        {
            OperatorId = operatorId,
            Balance = Money.Zero,
        };
    }

    public void Credit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        Balance += amount;
        RowVersion++;
    }

    public void Debit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (Balance < amount)
            throw new InvalidOperationException("Operator wallet balance cannot be negative.");

        Balance -= amount;
        RowVersion++;
    }
}
