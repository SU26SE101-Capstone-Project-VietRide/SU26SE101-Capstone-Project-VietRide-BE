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

    public Task<BatchChargeOutcome> BatchChargeAsync(
        Guid userId,
        string method,
        IReadOnlyList<BatchChargeItem> items,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(method, "WALLET", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BatchChargeOutcome>(new BatchChargeOutcome.TransportError(
                $"Unsupported payment method '{method}' for Day-13 batch development stub."));
        }

        if (items.Any(x => !string.Equals(x.ReferenceType, "BOOKING", StringComparison.Ordinal)))
        {
            return Task.FromResult<BatchChargeOutcome>(new BatchChargeOutcome.TransportError(
                "Day-13 batch development stub supports BOOKING references only."));
        }

        _logger.LogInformation(
            "Using Day-13 dev Payment stub for WALLET batch charge with {ItemCount} item(s).",
            items.Count);

        var payments = items
            .Select(x => new BatchChargePaymentResult(
                Guid.NewGuid(),
                x.ReferenceType,
                x.ReferenceId,
                "SUCCEEDED",
                null))
            .ToList();

        return Task.FromResult<BatchChargeOutcome>(new BatchChargeOutcome.Success(payments));
    }

    public Task<ChargeOutcome> ChargeAsync(
        string referenceType,
        Guid referenceId,
        Guid userId,
        long amount,
        string method,
        string idempotencyKey,
        CancellationToken cancellationToken = default,
        PaymentContextSnapshot? context = null,
        DateTimeOffset? dueAt = null)
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
