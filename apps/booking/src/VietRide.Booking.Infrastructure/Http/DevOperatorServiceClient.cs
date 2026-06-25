using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// Development stub for the Identity operator lookup seam.
/// Keeps Booking runnable locally when Identity is not available.
/// </summary>
public sealed class DevOperatorServiceClient : IOperatorServiceClient
{
    private readonly ILogger<DevOperatorServiceClient> _logger;

    public DevOperatorServiceClient(ILogger<DevOperatorServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<OperatorLookup?> GetOperatorAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using Day-17 dev Identity operator stub for operator {OperatorId}.",
            operatorId);

        var cancellationPolicy = JsonSerializer.SerializeToElement(new[]
        {
            new Dictionary<string, int>
            {
                ["hoursBeforeDeparture"] = 24,
                ["feePercent"] = 10,
            },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = new OperatorLookup(
            OperatorId: operatorId,
            Name: "Day-17 Dev Operator",
            RegistrationStatus: "APPROVED",
            IsActive: true,
            ContactEmail: "ops@example.com",
            ContactPhone: "+84901234567",
            BusinessRegistrationNumber: "0312345678",
            TaxCode: "0312345678",
            CancellationPolicy: cancellationPolicy);

        return Task.FromResult<OperatorLookup?>(result);
    }
}
