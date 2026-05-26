namespace VietRide.Shared.Kernel.ValueObjects;

/// VND money, stored as BIGINT (đồng). Floor to nearest 1,000 VND on creation
/// (per BACKEND_SOURCE_OF_TRUTH 4.4: "Floor 1,000 VND trước khi INSERT").
public readonly record struct Money(long Amount)
{
    public static Money Zero => new(0);

    public static Money FromRaw(long rawAmount)
    {
        if (rawAmount < 0) throw new ArgumentOutOfRangeException(nameof(rawAmount), "Money cannot be negative");
        return new Money(rawAmount - (rawAmount % 1000));
    }

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;

    public override string ToString() => $"{Amount:N0} VND";
}
