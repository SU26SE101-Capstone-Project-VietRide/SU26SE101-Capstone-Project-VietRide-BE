using FluentValidation;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed class DecideCustodyExceptionCommandValidator
    : AbstractValidator<DecideCustodyExceptionCommand>
{
    public DecideCustodyExceptionCommandValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.ReviewerUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.SubjectType)
            .Must(value => value is "PARCEL" or "INCIDENT")
            .WithMessage("SubjectType must be PARCEL or INCIDENT.");
        RuleFor(x => x.ReviewerRole)
            .Must(role => role is "DRIVER" or "OPERATOR_STAFF" or "OPERATOR_ADMIN")
            .WithMessage("Reviewer role is not allowed.");
        RuleFor(x => x.Decision)
            .Must(value => value is "APPROVE" or "REJECT")
            .WithMessage("Decision must be APPROVE or REJECT.");
        RuleFor(x => x)
            .Must(command => command.SubjectType switch
            {
                "PARCEL" => command.ReviewerRole == "DRIVER",
                "INCIDENT" => command.ReviewerRole is "OPERATOR_STAFF" or "OPERATOR_ADMIN",
                _ => false,
            })
            .WithMessage("Reviewer role is not allowed for this decision endpoint.");
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}
