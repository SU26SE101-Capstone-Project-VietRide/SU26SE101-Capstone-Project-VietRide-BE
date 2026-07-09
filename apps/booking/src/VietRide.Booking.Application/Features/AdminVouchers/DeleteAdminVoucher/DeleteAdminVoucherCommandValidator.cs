using FluentValidation;

namespace VietRide.Booking.Application.Features.AdminVouchers.DeleteAdminVoucher;

public sealed class DeleteAdminVoucherCommandValidator : AbstractValidator<DeleteAdminVoucherCommand>
{
    public DeleteAdminVoucherCommandValidator()
    {
        RuleFor(x => x.VoucherId)
            .NotEmpty();
    }
}
