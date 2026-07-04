using FluentValidation;

namespace VietRide.Booking.Application.Features.Manifest.GetTripManifest;

public sealed class GetTripManifestQueryValidator : AbstractValidator<GetTripManifestQuery>
{
    public GetTripManifestQueryValidator()
    {
        RuleFor(query => query.TripId).NotEmpty();
        RuleFor(query => query.CallerUserId).NotEmpty();
    }
}
