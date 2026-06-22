using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.VoucherConsents.AcceptVoucherConsent;

/// <summary>
/// Handles POST /v1/operator/voucher-consents/{id}/accept.
/// <para>
/// Happy path:
/// <list type="number">
///   <item>Fetch consent scoped to caller operator (cross-operator → 403 FORBIDDEN).</item>
///   <item>Verify precondition status = PENDING (otherwise → 409 CONSENT_NOT_PENDING).</item>
///   <item>Call <see cref="Domain.Entities.OperatorVoucherConsent.Accept"/> to transition status.</item>
///   <item>Enqueue <c>booking.voucher.consent_accepted</c> via Outbox (same transaction).</item>
///   <item>Persist and return result.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AcceptVoucherConsentCommandHandler
    : IRequestHandler<AcceptVoucherConsentCommand, AcceptVoucherConsentResult>
{
    private const string ConsentAcceptedEventType = "booking.voucher.consent_accepted";

    private readonly IOperatorVoucherConsentRepository _consents;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<AcceptVoucherConsentCommandHandler> _logger;

    public AcceptVoucherConsentCommandHandler(
        IOperatorVoucherConsentRepository consents,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<AcceptVoucherConsentCommandHandler> logger)
    {
        _consents = consents;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AcceptVoucherConsentResult> Handle(
        AcceptVoucherConsentCommand request,
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
        // 2. Precondition: status must be PENDING
        // -----------------------------------------------------------------------
        if (consent.Status != OperatorVoucherConsentStatus.PENDING)
        {
            throw new CodedConflictException(
                "CONSENT_NOT_PENDING",
                $"Consent '{request.ConsentId}' is in status '{consent.Status}' and cannot be accepted. Only PENDING consents can be accepted.");
        }

        // -----------------------------------------------------------------------
        // 3. Transition status (domain entity method)
        // -----------------------------------------------------------------------
        var respondedAt = _clock.UtcNow;
        consent.Accept(request.CallerUserId, respondedAt);
        _consents.Update(consent);

        // -----------------------------------------------------------------------
        // 4. Enqueue integration event (Outbox — same transaction)
        // -----------------------------------------------------------------------
        var payload = JsonSerializer.Serialize(new
        {
            voucherId = consent.VoucherId,
            operatorId = consent.OperatorId,
        });

        await _outbox.EnqueueAsync(ConsentAcceptedEventType, payload, cancellationToken);

        _logger.LogInformation(
            "Operator {OperatorId} accepted consent {ConsentId} for voucher {VoucherId}.",
            request.CallerOperatorId,
            request.ConsentId,
            consent.VoucherId);

        return new AcceptVoucherConsentResult(
            Id: consent.Id,
            Status: consent.Status.ToString());
    }
}
