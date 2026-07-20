using FluentValidation;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.Sent;

public sealed class GetSentParcelsQueryValidator : AbstractValidator<GetSentParcelsQuery>
{
    public GetSentParcelsQueryValidator()
    {
        RuleFor(query => query.UserId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1).WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.Status)
            .Must(status => status is null
                || Enum.GetNames<ParcelStatus>().Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage("status must be a valid ParcelStatus.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.From)
            .Must(ParcelHistoryDateRange.IsOptionalRfc3339)
            .WithMessage("from must be an RFC 3339 timestamp.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.To)
            .Must(ParcelHistoryDateRange.IsOptionalRfc3339)
            .WithMessage("to must be an RFC 3339 timestamp.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query)
            .Must(query => ParcelHistoryDateRange.IsOrdered(query.From, query.To))
            .WithName("from")
            .WithMessage("from must be earlier than to.")
            .WithErrorCode("VALIDATION_ERROR");
    }
}
