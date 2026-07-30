using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;

public sealed class ManualConfirmDeliveryCommandValidator : AbstractValidator<ManualConfirmDeliveryCommand>
{
    public ManualConfirmDeliveryCommandValidator()
    {
        RuleFor(command => command.Note)
            .Must(note => !string.IsNullOrWhiteSpace(note))
            .WithMessage("Confirm note is required.")
            .Must(note => note is null || note.Trim().Length <= 500)
            .WithMessage("Confirm note must be at most 500 characters.");
    }
}
