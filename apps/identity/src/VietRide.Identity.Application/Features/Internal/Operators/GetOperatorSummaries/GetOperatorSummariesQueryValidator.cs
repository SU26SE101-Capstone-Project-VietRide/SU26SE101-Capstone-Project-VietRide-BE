using FluentValidation;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorSummaries;

public sealed class GetOperatorSummariesQueryValidator : AbstractValidator<GetOperatorSummariesQuery>
{
    public GetOperatorSummariesQueryValidator()
    {
        RuleFor(query => query.OperatorIds).NotNull();
        RuleFor(query => query.OperatorIds)
            .Must(operatorIds => operatorIds is not null && operatorIds.Count <= 500)
            .WithMessage("At most 500 operator IDs are accepted.");
        RuleForEach(query => query.OperatorIds).NotEqual(Guid.Empty);
        RuleFor(query => query.OperatorIds)
            .Must(operatorIds => operatorIds is not null && operatorIds.Distinct().Count() == operatorIds.Count)
            .WithMessage("Operator IDs must be distinct.");
    }
}
