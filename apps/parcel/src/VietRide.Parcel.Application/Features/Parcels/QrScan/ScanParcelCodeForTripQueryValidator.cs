using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.QrScan;

public sealed class ScanParcelCodeForTripQueryValidator : AbstractValidator<ScanParcelCodeForTripQuery>
{
    private const string ParcelCodePattern =
        "^(?:VR-PCL-\\d{8}-[A-HJ-NP-Z2-9]{8}|VRP-\\d{8}-[A-Z0-9]{8})$";

    public ScanParcelCodeForTripQueryValidator()
    {
        RuleFor(query => query.TripId).NotEmpty();
        RuleFor(query => query.ParcelCode)
            .NotEmpty()
            .Matches(ParcelCodePattern)
            .WithMessage("ParcelCode must be a valid VietRide Parcel code.");
        RuleFor(query => query.AssistantUserId).NotEmpty();
        RuleFor(query => query.OperatorId).NotEmpty();
    }
}
