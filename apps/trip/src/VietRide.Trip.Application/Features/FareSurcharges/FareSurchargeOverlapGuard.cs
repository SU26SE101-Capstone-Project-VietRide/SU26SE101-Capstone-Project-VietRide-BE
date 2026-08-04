using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.FareSurcharges;

internal static class FareSurchargeOverlapGuard
{
    public static async Task EnsureAvailableAsync(
        IOperatorFareSurchargePeriodRepository repository,
        Guid operatorId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludedPeriodId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (!isActive)
            return;

        if (await repository.ExistsActiveOverlapAsync(
                operatorId,
                startDate,
                endDate,
                excludedPeriodId,
                cancellationToken))
        {
            throw new CodedValidationException(
                "FARE_SURCHARGE_PERIOD_OVERLAP",
                "Fare surcharge period overlaps an existing active period.",
                [
                    new ValidationError("startDate", "Active fare surcharge periods cannot overlap."),
                    new ValidationError("endDate", "Active fare surcharge periods cannot overlap."),
                ]);
        }
    }
}
