using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Application.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Features.TopUps.CreateTopUp;

public sealed class CreateTopUpCommandHandler : IRequestHandler<CreateTopUpCommand, CreateTopUpResult>
{
    private const long MinimumTopUpAmount = 10_000;
    private const string VnPayMethod = "VNPAY";

    private readonly ITopUpRequestRepository _topUpRequests;
    private readonly IVnPayClient _vnPayClient;
    private readonly IClock _clock;
    private readonly ILogger<CreateTopUpCommandHandler> _logger;

    public CreateTopUpCommandHandler(
        ITopUpRequestRepository topUpRequests,
        IVnPayClient vnPayClient,
        IClock clock,
        ILogger<CreateTopUpCommandHandler> logger)
    {
        _topUpRequests = topUpRequests;
        _vnPayClient = vnPayClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateTopUpResult> Handle(
        CreateTopUpCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Amount < MinimumTopUpAmount)
        {
            throw new CodedValidationException(
                "WALLET_TOP_UP_AMOUNT_TOO_LOW",
                "Top-up amount must be at least 10,000 VND.",
                [new ValidationError(nameof(request.Amount), "Top-up amount must be at least 10,000 VND.")]);
        }

        if (!string.Equals(request.Method, VnPayMethod, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "One or more validation errors occurred.",
                [new ValidationError(nameof(request.Method), "method must be VNPAY.")]);
        }

        if (string.IsNullOrWhiteSpace(request.PaymentReturnMode))
            throw new MobileAppUpdateRequiredException();

        if (!Enum.TryParse<VnPayReturnMode>(request.PaymentReturnMode, ignoreCase: true, out var returnMode)
            || returnMode != VnPayReturnMode.MOBILE_SDK)
        {
            throw new CodedValidationException(
                "PAYMENT_RETURN_MODE_INVALID",
                "paymentReturnMode must be MOBILE_SDK.",
                [new ValidationError(nameof(request.PaymentReturnMode), "paymentReturnMode must be MOBILE_SDK.")]);
        }

        var amount = Money.FromRaw(request.Amount);
        var vnPayTxnRef = Guid.NewGuid().ToString("D");
        var now = _clock.UtcNow;

        var redirectUrl = _vnPayClient.CreateTopUpRedirectUrl(
            request.UserId,
            amount,
            vnPayTxnRef,
            request.ClientIpAddress,
            now,
            returnMode);

        var topUpRequest = TopUpRequest.Create(
            request.UserId,
            amount,
            vnPayTxnRef,
            redirectUrl,
            returnMode);

        await _topUpRequests.AddAsync(topUpRequest, cancellationToken);

        _logger.LogInformation(
            "Created VNPay top-up request {TopUpRequestId} for user {UserId} with transaction reference {VnPayTxnRef}.",
            topUpRequest.Id,
            request.UserId,
            vnPayTxnRef);

        return new CreateTopUpResult(
            topUpRequest.Id,
            topUpRequest.Status.ToString(),
            redirectUrl,
            returnMode.ToString(),
            _vnPayClient.GetMobileSdkConfiguration());
    }
}
