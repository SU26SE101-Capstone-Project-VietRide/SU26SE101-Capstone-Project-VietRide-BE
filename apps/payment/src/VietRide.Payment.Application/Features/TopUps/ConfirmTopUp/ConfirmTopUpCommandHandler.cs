using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.TopUps.ConfirmTopUp;

public sealed class ConfirmTopUpCommandHandler : IRequestHandler<ConfirmTopUpCommand, ConfirmTopUpResult>
{
    private const string SuccessCode = "00";
    private const string SignatureInvalidCode = "PAYMENT_SIGNATURE_INVALID";
    private const string VnPayTxnRefKey = "vnp_TxnRef";
    private const string VnPayResponseCodeKey = "vnp_ResponseCode";
    private const string VnPayAmountKey = "vnp_Amount";
    private const string VnPayTransactionStatusKey = "vnp_TransactionStatus";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IVnPayClient _vnPayClient;
    private readonly ITopUpRequestRepository _topUpRequests;
    private readonly IWalletRepository _wallets;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmTopUpCommandHandler> _logger;

    public ConfirmTopUpCommandHandler(
        IVnPayClient vnPayClient,
        ITopUpRequestRepository topUpRequests,
        IWalletRepository wallets,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<ConfirmTopUpCommandHandler> logger)
    {
        _vnPayClient = vnPayClient;
        _topUpRequests = topUpRequests;
        _wallets = wallets;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ConfirmTopUpResult> Handle(
        ConfirmTopUpCommand request,
        CancellationToken cancellationToken)
    {
        if (!_vnPayClient.VerifySignature(request.Parameters))
        {
            return new ConfirmTopUpResult("97", SignatureInvalidCode, 401);
        }

        if (!request.Parameters.TryGetValue(VnPayTxnRefKey, out var vnPayTxnRef)
            || string.IsNullOrWhiteSpace(vnPayTxnRef))
        {
            return new ConfirmTopUpResult("01", "Order Not Found", 200);
        }

        var topUpRequest = await _topUpRequests.FindByVnPayTxnRefAsync(vnPayTxnRef, cancellationToken);
        if (topUpRequest is null)
        {
            _logger.LogWarning("VNPay top-up IPN references unknown transaction {VnPayTxnRef}.", vnPayTxnRef);
            return new ConfirmTopUpResult("01", "Order Not Found", 200);
        }

        if (topUpRequest.Status != TopUpRequestStatus.PENDING)
        {
            return ConfirmSuccess();
        }

        var reserved = await _vnPayClient.TryReserveIpnAsync(vnPayTxnRef, cancellationToken);
        if (!reserved)
        {
            var currentTopUpRequest = await _topUpRequests.FindByVnPayTxnRefAsync(vnPayTxnRef, cancellationToken);
            if (currentTopUpRequest is not null && currentTopUpRequest.Status != TopUpRequestStatus.PENDING)
            {
                return ConfirmSuccess();
            }

            _logger.LogWarning(
                "VNPay top-up IPN {VnPayTxnRef} was deduped while the top-up request is still pending.",
                vnPayTxnRef);
            return ConfirmPending();
        }

        try
        {
            var responseCode = request.Parameters.TryGetValue(VnPayResponseCodeKey, out var value)
                ? value
                : null;

            if (!string.Equals(responseCode, SuccessCode, StringComparison.Ordinal))
            {
                topUpRequest.MarkFailed(responseCode);
                _topUpRequests.Update(topUpRequest);
                return ConfirmSuccess();
            }

            if (!IsSignedAmountValid(request.Parameters, topUpRequest.Amount.Amount))
            {
                _logger.LogWarning(
                    "VNPay top-up IPN {VnPayTxnRef} has missing, invalid, or mismatched amount.",
                    vnPayTxnRef);
                topUpRequest.MarkFailed(responseCode);
                _topUpRequests.Update(topUpRequest);
                return ConfirmFailure();
            }

            if (request.Parameters.TryGetValue(VnPayTransactionStatusKey, out var transactionStatus)
                && !string.Equals(transactionStatus, SuccessCode, StringComparison.Ordinal))
            {
                topUpRequest.MarkFailed(transactionStatus);
                _topUpRequests.Update(topUpRequest);
                return ConfirmSuccess();
            }

            topUpRequest.MarkSucceeded(responseCode, _clock.UtcNow);
            _topUpRequests.Update(topUpRequest);

            await _wallets.CreditTopUpAsync(
                topUpRequest.UserId,
                topUpRequest.Amount,
                topUpRequest.Id,
                cancellationToken);

            var integrationEvent = new WalletCreditedIntegrationEvent(
                topUpRequest.UserId,
                topUpRequest.Amount.Amount,
                WalletTransactionRef.TOP_UP.ToString(),
                topUpRequest.Id);

            var payload = JsonSerializer.Serialize(new
            {
                integrationEvent.UserId,
                integrationEvent.Amount,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId,
            }, JsonOptions);

            await _outbox.EnqueueAsync(WalletCreditedIntegrationEvent.EventTypeValue, payload, cancellationToken);

            _logger.LogInformation(
                "Confirmed VNPay top-up {TopUpRequestId} for user {UserId} and credited {Amount} VND.",
                topUpRequest.Id,
                topUpRequest.UserId,
                topUpRequest.Amount.Amount);

            return ConfirmSuccess();
        }
        catch (Exception ex)
        {
            try
            {
                await _vnPayClient.ReleaseIpnReservationAsync(vnPayTxnRef, CancellationToken.None);
            }
            catch (Exception releaseEx)
            {
                _logger.LogWarning(
                    releaseEx,
                    "Failed to release VNPay IPN reservation for {VnPayTxnRef} after processing error.",
                    vnPayTxnRef);
            }

            _logger.LogWarning(
                ex,
                "Failed to process VNPay top-up IPN {VnPayTxnRef} after reservation; reservation released so it can retry.",
                vnPayTxnRef);
            throw;
        }
    }

    private static bool IsSignedAmountValid(IReadOnlyDictionary<string, string> parameters, long topUpAmount)
    {
        if (!parameters.TryGetValue(VnPayAmountKey, out var signedAmount)
            || !long.TryParse(signedAmount, out var parsedSignedAmount))
        {
            return false;
        }

        try
        {
            return parsedSignedAmount == checked(topUpAmount * 100);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static ConfirmTopUpResult ConfirmSuccess()
        => new(SuccessCode, "Confirm Success", 200);

    private static ConfirmTopUpResult ConfirmFailure()
        => new("99", "Confirm Failed", 200);

    private static ConfirmTopUpResult ConfirmPending()
        => new("99", "Order Processing", 200);
}
