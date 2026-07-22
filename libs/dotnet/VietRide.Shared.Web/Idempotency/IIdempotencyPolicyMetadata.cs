namespace VietRide.Shared.Web.Idempotency;

/// <summary>
/// Describes an endpoint's idempotency requirement for runtime and OpenAPI consumers.
/// Implementing this interface does not, by itself, change middleware behavior.
/// </summary>
public interface IIdempotencyPolicyMetadata
{
    bool IsRequired { get; }
}
