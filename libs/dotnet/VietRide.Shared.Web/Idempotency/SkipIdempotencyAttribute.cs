namespace VietRide.Shared.Web.Idempotency;

/// <summary>
/// Exempts a mutation endpoint that already has an approved replay/deduplication mechanism,
/// or a POST endpoint that is semantically read-only, from HTTP idempotency middleware.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SkipIdempotencyAttribute : Attribute
{
    public SkipIdempotencyAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("An idempotency exemption reason is required.", nameof(reason));

        Reason = reason;
    }

    public string Reason { get; }
}
