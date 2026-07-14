namespace VietRide.Shared.Web.Idempotency;

/// <summary>Marks an endpoint as requiring a UUID-v4 Idempotency-Key header.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireIdempotencyAttribute : Attribute
{
}
