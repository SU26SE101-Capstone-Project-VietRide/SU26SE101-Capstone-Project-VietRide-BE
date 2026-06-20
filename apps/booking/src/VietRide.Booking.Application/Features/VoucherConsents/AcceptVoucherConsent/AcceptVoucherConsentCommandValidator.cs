using FluentValidation;

namespace VietRide.Booking.Application.Features.VoucherConsents.AcceptVoucherConsent;

/// <summary>
/// Input validation for <see cref="AcceptVoucherConsentCommand"/>.
/// </summary>
public sealed class AcceptVoucherConsentCommandValidator : AbstractValidator<AcceptVoucherConsentCommand>
{
    public AcceptVoucherConsentCommandValidator()
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
    }
}
