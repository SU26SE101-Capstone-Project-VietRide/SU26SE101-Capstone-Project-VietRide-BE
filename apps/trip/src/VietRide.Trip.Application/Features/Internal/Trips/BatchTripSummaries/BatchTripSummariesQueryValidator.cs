using FluentValidation;

namespace VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

public sealed class BatchTripSummariesQueryValidator : AbstractValidator<BatchTripSummariesQuery>
{
    private const int MaximumTripIds = 100;

    public BatchTripSummariesQueryValidator()
    {
        RuleFor(query => query.TripIds)
            .NotNull()
            .Must(tripIds => tripIds.Count is >= 1 and <= MaximumTripIds)
            .WithMessage($"TripIds must contain between 1 and {MaximumTripIds} values.")
            .Must(tripIds => tripIds.All(tripId => tripId != Guid.Empty))
            .WithMessage("TripIds must not contain an empty UUID.")
            .Must(tripIds => tripIds.Distinct().Count() == tripIds.Count)
            .WithMessage("TripIds must contain distinct values.");
    }
}
