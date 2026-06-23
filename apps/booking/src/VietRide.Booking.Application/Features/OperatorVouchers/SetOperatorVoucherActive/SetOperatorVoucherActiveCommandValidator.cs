using FluentValidation;

namespace VietRide.Booking.Application.Features.OperatorVouchers.SetOperatorVoucherActive;

/// <summary>
/// Input-shape validation for <see cref="SetOperatorVoucherActiveCommand"/>.
/// </summary>
public sealed class SetOperatorVoucherActiveCommandValidator : AbstractValidator<SetOperatorVoucherActiveCommand>
{
    public SetOperatorVoucherActiveCommandValidator()
    {
        RuleFor(x => x.VoucherId)
            .NotEmpty();

        RuleFor(x => x.CallerOperatorId)
            .NotEmpty();
    }
}
