using FluentValidation;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Input validation for <see cref="ListAdminVoucherConsentsQuery"/>.
/// </summary>
public sealed class ListAdminVoucherConsentsQueryValidator : AbstractValidator<ListAdminVoucherConsentsQuery>
{
    public ListAdminVoucherConsentsQueryValidator()
    {
        RuleFor(x => x.VoucherId)
            .NotEmpty()
            .WithMessage("Voucher id must not be empty.");
    }
}
