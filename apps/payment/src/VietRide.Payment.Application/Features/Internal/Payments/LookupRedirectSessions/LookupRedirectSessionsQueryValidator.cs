using FluentValidation;

namespace VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;

public sealed class LookupRedirectSessionsQueryValidator : AbstractValidator<LookupRedirectSessionsQuery>
{
    private static readonly HashSet<string> AllowedReferenceTypes = new(StringComparer.Ordinal)
    {
        "BOOKING",
        "BOOKING_GROUP",
        "PARCEL",
        "PARCEL_ADDITIONAL",
    };

    public LookupRedirectSessionsQueryValidator()
    {
        RuleFor(query => query.UserId).NotEmpty();
        RuleFor(query => query.References)
            .NotNull()
            .Must(references => references.Count is >= 1 and <= 100)
            .WithMessage("Between 1 and 100 references are required.")
            .Must(HaveUniqueCompositeReferences)
            .WithMessage("References must be unique by referenceType and referenceId.");

        RuleForEach(query => query.References).ChildRules(reference =>
        {
            reference.RuleFor(item => item.ReferenceType)
                .Must(AllowedReferenceTypes.Contains)
                .WithMessage("Reference type is not supported.");
            reference.RuleFor(item => item.ReferenceId).NotEmpty();
        });
    }

    private static bool HaveUniqueCompositeReferences(
        IReadOnlyList<LookupRedirectSessionsQuery.Reference> references)
        => references.DistinctBy(reference => (reference.ReferenceType, reference.ReferenceId)).Count()
            == references.Count;
}
