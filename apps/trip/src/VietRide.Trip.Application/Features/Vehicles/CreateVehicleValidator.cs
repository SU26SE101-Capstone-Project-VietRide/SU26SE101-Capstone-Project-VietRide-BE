using FluentValidation;
using VietRide.Shared.Application.Security;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class CreateVehicleValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleValidator(IFirebaseStorageImageUrlValidator firebaseUrls)
    {
        RuleFor(command => command.OperatorId).NotEmpty();
        RuleFor(command => command.VehicleTypeId).NotEmpty();
        RuleFor(command => command.LicensePlate).NotEmpty().MaximumLength(20);
        RuleFor(command => command.SeatLayoutJson).NotNull();
        RuleFor(command => command.TotalSeats).GreaterThan(0);
        RuleFor(command => command.MaxCargoWeightKg).GreaterThanOrEqualTo(0).When(command => command.MaxCargoWeightKg.HasValue);
        RuleFor(command => command.MaxCargoVolumeM3).GreaterThanOrEqualTo(0).When(command => command.MaxCargoVolumeM3.HasValue);
        RuleFor(command => command)
            .Must(command => BeValidImageUrls(
                command.ImageUrls,
                firebaseUrls,
                $"vehicles/{command.OperatorId:D}/"))
            .WithMessage("ImageUrls must contain owned Firebase vehicle image URLs.")
            .When(command => command.ImageUrls is not null);
    }

    private static bool BeValidImageUrls(
        IReadOnlyCollection<string>? urls,
        IFirebaseStorageImageUrlValidator firebaseUrls,
        string expectedPrefix) => urls is null || urls.Count <= 5
        && urls.All(url => firebaseUrls.IsValidOwnedImageUrl(url, expectedPrefix))
        && urls.Distinct(StringComparer.OrdinalIgnoreCase).Count() == urls.Count;
}
