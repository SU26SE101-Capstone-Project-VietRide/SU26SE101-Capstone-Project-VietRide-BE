using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;

public sealed class LookupRedirectSessionsQueryHandler
    : IRequestHandler<LookupRedirectSessionsQuery, IReadOnlyList<LookupRedirectSessionsResult>>
{
    private readonly IPaymentRepository _payments;
    private readonly IVnPayRedirectUrlValidator _urlValidator;
    private readonly IClock _clock;

    public LookupRedirectSessionsQueryHandler(
        IPaymentRepository payments,
        IVnPayRedirectUrlValidator urlValidator,
        IClock clock)
    {
        _payments = payments;
        _urlValidator = urlValidator;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LookupRedirectSessionsResult>> Handle(
        LookupRedirectSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var references = request.References
            .Select(reference => new PaymentReference(
                Enum.Parse<PaymentReferenceType>(reference.ReferenceType, ignoreCase: false),
                reference.ReferenceId))
            .ToArray();

        var candidates = await _payments.ListLatestRedirectSessionCandidatesAsync(
            references,
            cancellationToken).ConfigureAwait(false);
        var candidatesByReference = candidates.ToDictionary(
            candidate => (candidate.ReferenceType, candidate.ReferenceId));
        var results = new List<LookupRedirectSessionsResult>(candidates.Count);

        foreach (var reference in references)
        {
            if (!candidatesByReference.TryGetValue((reference.ReferenceType, reference.ReferenceId), out var candidate)
                || !IsEligible(candidate, request.UserId))
            {
                continue;
            }

            results.Add(new LookupRedirectSessionsResult(
                candidate.PaymentId,
                candidate.ReferenceType.ToString(),
                candidate.ReferenceId,
                candidate.Amount,
                candidate.DueAt!.Value,
                candidate.PaymentRedirectUrl!));
        }

        return results;
    }

    private bool IsEligible(RedirectSessionLookupCandidate candidate, Guid userId)
        => candidate.UserId == userId
            && candidate.Method == PaymentMethod.VNPAY
            && candidate.Status == PaymentStatus.PENDING_REDIRECT
            && candidate.DueAt is not null
            && candidate.DueAt > _clock.UtcNow
            && HasTrustedContext(candidate)
            && _urlValidator.IsTrusted(candidate.PaymentRedirectUrl);

    private static bool HasTrustedContext(RedirectSessionLookupCandidate candidate)
    {
        if (candidate.ContextReconciliationRequired || PaymentContextCodec.IsMissing(candidate.Context))
            return false;

        try
        {
            var context = PaymentContextCodec.DeserializeTrusted(candidate.Context);
            _ = PaymentContextCodec.ValidateAndSerialize(
                context,
                candidate.ReferenceType.ToString(),
                candidate.ReferenceId,
                candidate.Amount);
            return true;
        }
        catch (CodedValidationException)
        {
            return false;
        }
    }
}
