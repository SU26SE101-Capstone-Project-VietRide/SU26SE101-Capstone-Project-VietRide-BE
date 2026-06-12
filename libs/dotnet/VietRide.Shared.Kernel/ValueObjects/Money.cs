namespace VietRide.Shared.Kernel.ValueObjects;

/// VND money, stored as BIGINT (đồng). Amounts are kept to the đồng — no rounding
/// to thousands. Fractional results (e.g. percentage discounts) round to the
/// nearest đồng via FromDecimal (per BACKEND_SOURCE_OF_TRUTH 4.4, v1.11.0).
public readonly record struct Money(long Amount)
{
    public static Money Zero => new(0);

    public static Money FromRaw(long rawAmount)
    {
        if (rawAmount < 0) throw new ArgumentOutOfRangeException(nameof(rawAmount), "Money cannot be negative");
        return new Money(rawAmount);
    }

    public static Money FromDecimal(decimal rawAmount)
    {
        if (rawAmount < 0) throw new ArgumentOutOfRangeException(nameof(rawAmount), "Money cannot be negative");
        return new Money((long)Math.Round(rawAmount, 0, MidpointRounding.AwayFromZero));
    }

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;

    public override string ToString() => $"{Amount:N0} VND";
}
