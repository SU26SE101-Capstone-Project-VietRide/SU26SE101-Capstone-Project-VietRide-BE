using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class DevPaymentServiceClient : IPaymentServiceClient
{
    private readonly ILogger<DevPaymentServiceClient> _logger;

    public DevPaymentServiceClient(ILogger<DevPaymentServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<ChargeOutcome> ChargeParcelPaymentAsync(
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
                "Using dev Payment stub for WALLET charge {ReferenceType}/{ReferenceId} amount {Amount}.",
                referenceType, referenceId, amount);

            return Task.FromResult(new ChargeOutcome(
                ChargeOutcomeKind.Success,
                new ChargeResult(Guid.NewGuid(), "SUCCEEDED", null),
                null));
        }

        if (string.Equals(method, "VNPAY", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Using dev Payment stub for VNPAY charge {ReferenceType}/{ReferenceId} amount {Amount}.",
                referenceType, referenceId, amount);

            return Task.FromResult(new ChargeOutcome(
                ChargeOutcomeKind.Success,
                new ChargeResult(Guid.NewGuid(), "PENDING",
                    $"https://sandbox.vnpay.vn/paymentv2/vpcpay.html?referenceId={referenceId:N}"),
                null));
        }

        return Task.FromResult(new ChargeOutcome(
            ChargeOutcomeKind.TransportError, null,
            $"Unsupported payment method '{method}' for dev stub."));
    }

    public Task<RefundOutcome> RefundParcelPaymentAsync(
        Guid userId,
        long amount,
        string referenceType,
        Guid referenceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using dev Payment stub for refund {ReferenceType}/{ReferenceId} amount {Amount}.",
            referenceType, referenceId, amount);

        return Task.FromResult(new RefundOutcome(
            RefundOutcomeKind.Success,
            new RefundResult(Guid.NewGuid(), amount),
            null));
    }
}
