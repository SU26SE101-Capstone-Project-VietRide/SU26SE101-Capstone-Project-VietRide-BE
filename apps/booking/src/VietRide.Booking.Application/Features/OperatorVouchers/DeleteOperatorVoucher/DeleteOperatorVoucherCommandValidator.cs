using FluentValidation;

namespace VietRide.Booking.Application.Features.OperatorVouchers.DeleteOperatorVoucher;

/// <summary>
/// Input-shape validation for <see cref="DeleteOperatorVoucherCommand"/>.
/// </summary>
public sealed class DeleteOperatorVoucherCommandValidator : AbstractValidator<DeleteOperatorVoucherCommand>
{
    public DeleteOperatorVoucherCommandValidator()
    {
        RuleFor(x => x.VoucherId)
            .NotEmpty();

        RuleFor(x => x.CallerOperatorId)
            .NotEmpty();
    }
}
