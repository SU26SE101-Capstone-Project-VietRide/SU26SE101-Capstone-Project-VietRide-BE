namespace VietRide.Shared.Web.Idempotency;

/// <summary>
/// Documents an existing controller-level idempotency requirement without changing runtime behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IdempotencyOpenApiAttribute : Attribute, IIdempotencyPolicyMetadata
{
    public bool IsRequired => true;
}
