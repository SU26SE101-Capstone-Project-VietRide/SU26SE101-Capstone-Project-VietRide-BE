namespace VietRide.Booking.Domain.Services;

public readonly record struct CancellationPolicyTier
{
    public CancellationPolicyTier(decimal hoursBeforeDeparture, decimal feePercent)
    {
        if (hoursBeforeDeparture < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hoursBeforeDeparture), "Hours before departure cannot be negative.");
        }

        if (feePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(feePercent), "Fee percent must be between 0 and 100.");
        }

        HoursBeforeDeparture = hoursBeforeDeparture;
        FeePercent = feePercent;
    }

    public decimal HoursBeforeDeparture { get; }

    public decimal FeePercent { get; }
}
