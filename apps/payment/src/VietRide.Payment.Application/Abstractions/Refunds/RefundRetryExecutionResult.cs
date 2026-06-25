namespace VietRide.Payment.Application.Abstractions.Refunds;

public sealed record RefundRetryExecutionResult(bool Succeeded, string? FailureReason)
{
    public static RefundRetryExecutionResult Success()
        => new(true, null);

    public static RefundRetryExecutionResult Failure(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));

        return new RefundRetryExecutionResult(false, failureReason);
    }
}
