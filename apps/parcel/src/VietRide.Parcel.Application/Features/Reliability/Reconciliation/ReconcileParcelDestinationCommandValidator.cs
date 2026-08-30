using FluentValidation;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class ReconcileParcelDestinationCommandValidator
    : AbstractValidator<ReconcileParcelDestinationCommand>
{
    public ReconcileParcelDestinationCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}
