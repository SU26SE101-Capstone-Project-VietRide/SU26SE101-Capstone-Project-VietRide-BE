using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// HTTP client implementation for the Identity operator lookup seam.
/// </summary>
public sealed class OperatorServiceClient : IOperatorServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public OperatorServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<OperatorLookup?> GetOperatorAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync($"/internal/v1/operators/{operatorId:D}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<OperatorLookup>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
