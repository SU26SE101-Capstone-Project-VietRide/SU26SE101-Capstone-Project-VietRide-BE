using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed class AvailableTripsQueryValidator : AbstractValidator<AvailableTripsQuery>
{
    public AvailableTripsQueryValidator()
    {
        RuleFor(x => x.OriginStationId)
            .NotEmpty();

        RuleFor(x => x)
            .Custom((query, context) =>
            {
                if (query.DestinationStationId == Guid.Empty)
                    context.AddFailure(nameof(query.DestinationStationId), "DestinationStationId must not be empty.");
                if (query.DropoffStopId == Guid.Empty)
                    context.AddFailure(nameof(query.DropoffStopId), "DropoffStopId must not be empty.");

                var modeCount = (query.DestinationStationId.HasValue ? 1 : 0)
                    + (query.DropoffStopId.HasValue ? 1 : 0)
                    + (!string.IsNullOrWhiteSpace(query.DestinationProvinceCode) ? 1 : 0);
                if (modeCount != 1)
                {
                    context.AddFailure(
                        nameof(query.DestinationStationId),
                        "Exactly one destination mode must be supplied: DestinationStationId, DropoffStopId, or DestinationProvinceCode.");
                }

                if (!string.IsNullOrWhiteSpace(query.DestinationLocationCode)
                    && string.IsNullOrWhiteSpace(query.DestinationProvinceCode))
                {
                    context.AddFailure(
                        nameof(query.DestinationLocationCode),
                        "DestinationLocationCode requires DestinationProvinceCode.");
                }
            });

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
