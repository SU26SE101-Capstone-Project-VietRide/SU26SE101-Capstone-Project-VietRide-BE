using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// Day-12 development stub for the Payment charge seam.
/// Keeps Booking wired through <see cref="IPaymentServiceClient"/> before the real Payment
/// Day-15/16 charge endpoint is available.
/// </summary>
public sealed class DevPaymentServiceClient : IPaymentServiceClient
{
    private readonly ILogger<DevPaymentServiceClient> _logger;

    public DevPaymentServiceClient(ILogger<DevPaymentServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<ChargeOutcome> ChargeAsync(
        string referenceType,
        Guid referenceId,
        Guid userId,
        long amount,
        string method,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(method, "WALLET", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Using Day-12 dev Payment stub for WALLET charge {ReferenceType}/{ReferenceId} amount {Amount}.",
                referenceType,
                referenceId,
                amount);

            return Task.FromResult<ChargeOutcome>(new ChargeOutcome.Success(
                new ChargeResult(Guid.NewGuid(), "SUCCEEDED", null)));
        }

        if (string.Equals(method, "VNPAY", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Using Day-12 dev Payment stub for VNPAY charge {ReferenceType}/{ReferenceId} amount {Amount}.",
                referenceType,
                referenceId,
                amount);

            return Task.FromResult<ChargeOutcome>(new ChargeOutcome.Success(
                new ChargeResult(
                    Guid.NewGuid(),
                    "PENDING",
                    $"https://sandbox.vnpay.vn/paymentv2/vpcpay.html?referenceId={referenceId:N}")));
        }

        return Task.FromResult<ChargeOutcome>(new ChargeOutcome.TransportError(
            $"Unsupported payment method '{method}' for Day-12 development stub."));
    }
}
