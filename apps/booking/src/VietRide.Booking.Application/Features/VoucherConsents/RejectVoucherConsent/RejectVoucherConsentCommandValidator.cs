using FluentValidation;

namespace VietRide.Booking.Application.Features.VoucherConsents.RejectVoucherConsent;

/// <summary>
/// Input validation for <see cref="RejectVoucherConsentCommand"/>.
/// </summary>
public sealed class RejectVoucherConsentCommandValidator : AbstractValidator<RejectVoucherConsentCommand>
{
    public RejectVoucherConsentCommandValidator()
    {
        RuleFor(x => x.ConsentId)
            .NotEmpty()
            .WithMessage("Consent id must not be empty.");

        RuleFor(x => x.CallerOperatorId)
            .NotEmpty()
            .WithMessage("Caller operator id must not be empty.");

        RuleFor(x => x.CallerUserId)
            .NotEmpty()
            .WithMessage("Caller user id must not be empty.");

        RuleFor(x => x.Reason)
            .MaximumLength(2000)
            .When(x => x.Reason is not null)
            .WithMessage("Reason must not exceed 2000 characters.");
    }
}
