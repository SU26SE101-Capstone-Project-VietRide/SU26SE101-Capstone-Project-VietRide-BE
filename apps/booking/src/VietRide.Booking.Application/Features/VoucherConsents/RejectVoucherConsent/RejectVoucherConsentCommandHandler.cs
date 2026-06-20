using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.VoucherConsents.RejectVoucherConsent;

/// <summary>
/// Handles POST /v1/operator/voucher-consents/{id}/reject.
/// <para>
/// Happy path:
/// <list type="number">
///   <item>Fetch consent scoped to caller operator (cross-operator → 403 FORBIDDEN).</item>
///   <item>Verify precondition status IN (PENDING, ACCEPTED) (otherwise → 409 CONSENT_ALREADY_REJECTED).</item>
///   <item>Call <see cref="Domain.Entities.OperatorVoucherConsent.Reject"/> to transition status.</item>
///   <item>Enqueue <c>booking.voucher.consent_rejected</c> via Outbox (same transaction).</item>
///   <item>Persist and return result.</item>
/// </list>
/// </para>
/// <para>
/// Revoke semantics (ACCEPTED → REJECTED): does NOT roll back discounts on already-CONFIRMED bookings.
/// Future bookings after <c>respondedAt</c> will not apply the voucher for this operator.
/// </para>
/// </summary>
public sealed class RejectVoucherConsentCommandHandler
    : IRequestHandler<RejectVoucherConsentCommand, RejectVoucherConsentResult>
{
    private const string ConsentRejectedEventType = "booking.voucher.consent_rejected";

    private readonly IOperatorVoucherConsentRepository _consents;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<RejectVoucherConsentCommandHandler> _logger;

    public RejectVoucherConsentCommandHandler(
        IOperatorVoucherConsentRepository consents,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<RejectVoucherConsentCommandHandler> logger)
    {
        _consents = consents;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RejectVoucherConsentResult> Handle(
        RejectVoucherConsentCommand request,
        CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // 1. Fetch consent scoped to caller operator (tenant isolation)
        // -----------------------------------------------------------------------
        var consent = await _consents.FindByIdAndOperatorAsync(
            request.ConsentId,
            request.CallerOperatorId,
            cancellationToken);

        if (consent is null)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                $"Consent '{request.ConsentId}' does not exist or does not belong to operator '{request.CallerOperatorId}'.");
        }

        // -----------------------------------------------------------------------
        // 2. Precondition: status must be PENDING or ACCEPTED
        // -----------------------------------------------------------------------
        if (consent.Status == OperatorVoucherConsentStatus.REJECTED)
        {
            throw new CodedConflictException(
                "CONSENT_ALREADY_REJECTED",
                $"Consent '{request.ConsentId}' is already REJECTED and cannot be rejected again.");
        }

        // -----------------------------------------------------------------------
        // 3. Transition status (domain entity method)
        // -----------------------------------------------------------------------
        var respondedAt = _clock.UtcNow;
        consent.Reject(request.CallerUserId, respondedAt, request.Reason);
        _consents.Update(consent);

        // -----------------------------------------------------------------------
        // 4. Enqueue integration event (Outbox — same transaction)
        // -----------------------------------------------------------------------
        var payload = JsonSerializer.Serialize(new
        {
            voucherId = consent.VoucherId,
            operatorId = consent.OperatorId,
            reason = consent.RejectReason,
        });

        await _outbox.EnqueueAsync(ConsentRejectedEventType, payload, cancellationToken);

        _logger.LogInformation(
            "Operator {OperatorId} rejected consent {ConsentId} for voucher {VoucherId}.",
            request.CallerOperatorId,
            request.ConsentId,
            consent.VoucherId);

        return new RejectVoucherConsentResult(
            Id: consent.Id,
            Status: consent.Status);
    }
}
