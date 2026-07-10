using FluentValidation;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class CreateVehicleValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleValidator()
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.VehicleTypeId).NotEmpty();
        RuleFor(command => command.LicensePlate).NotEmpty().MaximumLength(20);
        RuleFor(command => command.SeatLayoutJson).NotNull();
        RuleFor(command => command.TotalSeats).GreaterThan(0);
        RuleFor(command => command.MaxCargoWeightKg).GreaterThanOrEqualTo(0).When(command => command.MaxCargoWeightKg.HasValue);
        RuleFor(command => command.MaxCargoVolumeM3).GreaterThanOrEqualTo(0).When(command => command.MaxCargoVolumeM3.HasValue);
        RuleFor(command => command.ImageUrls).Must(BeValidImageUrls).When(command => command.ImageUrls is not null);
    }

    private static bool BeValidImageUrls(IReadOnlyCollection<string>? urls) => urls is null || urls.Count <= 5
        && urls.All(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        && urls.Distinct(StringComparer.OrdinalIgnoreCase).Count() == urls.Count;
}
