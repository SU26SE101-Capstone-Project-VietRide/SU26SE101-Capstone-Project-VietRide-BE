using System.Text.Json;

namespace VietRide.Booking.Application.Abstractions.ServiceClients;

/// <summary>
/// Operator lookup returned by Identity GET /internal/v1/operators/{operatorId}.
/// Raw DTO, no ApiResponse envelope.
/// </summary>
public sealed record OperatorLookup(
    Guid OperatorId,
    string Name,
    string RegistrationStatus,
    bool IsActive,
    string ContactEmail,
    string ContactPhone,
    string BusinessRegistrationNumber,
    string TaxCode,
    JsonElement? CancellationPolicy);

/// <summary>
/// Application-facing seam for the Identity operator internal lookup.
/// </summary>
public interface IOperatorServiceClient
{
    /// <summary>
    /// GET /internal/v1/operators/{operatorId}. Returns <c>null</c> if Identity returns 404.
    /// </summary>
    Task<OperatorLookup?> GetOperatorAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);
}
