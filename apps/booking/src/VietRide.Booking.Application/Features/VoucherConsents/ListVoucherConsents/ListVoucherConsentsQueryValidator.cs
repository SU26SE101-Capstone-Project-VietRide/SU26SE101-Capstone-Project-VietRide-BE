using FluentValidation;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Input validation for <see cref="ListVoucherConsentsQuery"/>.
/// </summary>
public sealed class ListVoucherConsentsQueryValidator : AbstractValidator<ListVoucherConsentsQuery>
{
    public ListVoucherConsentsQueryValidator()
    {
        RuleFor(x => x.CallerOperatorId)
            .NotEmpty()
            .WithMessage("Caller operator id must not be empty.");
    }
}
