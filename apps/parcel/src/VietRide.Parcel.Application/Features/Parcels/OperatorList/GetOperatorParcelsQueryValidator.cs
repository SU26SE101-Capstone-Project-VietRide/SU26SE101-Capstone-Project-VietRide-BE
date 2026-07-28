using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed class GetOperatorParcelsQueryValidator : AbstractValidator<GetOperatorParcelsQuery>
{
    public GetOperatorParcelsQueryValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Status)
            .Must(IsOptionalDefinedEnum<ParcelStatus>)
            .WithMessage("Status must be a valid ParcelStatus value when provided.");
        RuleFor(query => query.TripId)
            .Must(tripId => !tripId.HasValue || tripId.Value != Guid.Empty)
            .WithMessage("TripId must not be empty when provided.");
        RuleFor(query => query.PendingActionType)
            .Must(IsOptionalDefinedEnum<PendingActionType>)
            .WithMessage("PendingActionType must be valid when provided.");
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }

    private static bool IsOptionalDefinedEnum<TEnum>(string? value)
        where TEnum : struct, Enum
        => string.IsNullOrWhiteSpace(value)
            || (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed));
}
