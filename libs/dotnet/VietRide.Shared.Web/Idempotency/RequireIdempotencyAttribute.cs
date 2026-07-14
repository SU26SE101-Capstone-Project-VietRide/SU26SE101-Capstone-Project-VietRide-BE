namespace VietRide.Shared.Web.Idempotency;

/// <summary>Marks an endpoint as requiring a UUID-v4 Idempotency-Key header.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireIdempotencyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether the annotated endpoint accepts a request body. The default preserves
    /// the behavior of existing idempotent endpoints.
    /// </summary>
    public bool AllowRequestBody { get; init; } = true;
}
