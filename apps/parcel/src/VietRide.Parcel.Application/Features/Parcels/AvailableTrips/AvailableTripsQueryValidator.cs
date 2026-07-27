using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed class AvailableTripsQueryValidator : AbstractValidator<AvailableTripsQuery>
{
    public AvailableTripsQueryValidator()
    {
        RuleFor(x => x.OriginStationId)
            .NotEmpty();

        RuleFor(x => x.DestinationStationId)
            .NotEmpty();

        RuleFor(x => x.DepartureDate)
            .NotEmpty()
            .Must(d => d != default(DateOnly));

        RuleFor(x => x.EstimatedWeightKg)
            .GreaterThan(0);

        RuleFor(x => x.LengthCm)
            .GreaterThan(0);

        RuleFor(x => x.WidthCm)
            .GreaterThan(0);

        RuleFor(x => x.HeightCm)
            .GreaterThan(0);

        RuleFor(x => x.SizeCategory)
            .Must(v => string.IsNullOrWhiteSpace(v)
                || Enum.TryParse<ParcelSizeCategory>(v, ignoreCase: true, out _))
            .WithMessage("SizeCategory must be a valid ParcelSizeCategory value when provided.");

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);
    }
}
