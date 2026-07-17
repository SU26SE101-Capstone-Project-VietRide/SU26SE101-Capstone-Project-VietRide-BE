using FluentValidation;

namespace VietRide.Trip.Application.Features.Trips.EditTrip;

public sealed class EditTripValidator : AbstractValidator<EditTripCommand>
{
    public EditTripValidator()
    {
        RuleFor(command => command.TripId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.OperatorId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.ActorUserId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(command => command.RequestId).NotEmpty().WithErrorCode("VALIDATION_ERROR");

        RuleFor(command => command)
            .Must(HasRecognizedField)
            .WithMessage("At least one editable Trip field must be supplied.")
            .WithErrorCode("VALIDATION_ERROR");

        When(command => command.BaseFareSpecified, () =>
            RuleFor(command => command.BaseFare)
                .NotNull()
                .WithErrorCode("VALIDATION_ERROR")
                .GreaterThanOrEqualTo(0)
                .WithErrorCode("VALIDATION_ERROR"));
        When(command => command.VehicleIdSpecified, () =>
            RuleFor(command => command.VehicleId)
                .NotNull()
                .WithErrorCode("VALIDATION_ERROR")
                .NotEqual(Guid.Empty)
                .WithErrorCode("VALIDATION_ERROR"));
        When(command => command.RouteIdSpecified, () =>
            RuleFor(command => command.RouteId)
                .NotNull()
                .WithErrorCode("VALIDATION_ERROR")
                .NotEqual(Guid.Empty)
                .WithErrorCode("VALIDATION_ERROR"));
        When(command => command.NotesSpecified && command.Notes is not null, () =>
            RuleFor(command => command.Notes)
                .MaximumLength(2000)
                .WithErrorCode("VALIDATION_ERROR"));
    }

    private static bool HasRecognizedField(EditTripCommand command) =>
        command.BaseFareSpecified
        || command.NotesSpecified
        || command.VehicleIdSpecified
        || command.RouteIdSpecified;
}
