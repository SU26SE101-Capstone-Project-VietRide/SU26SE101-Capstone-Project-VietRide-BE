using FluentValidation;

namespace VietRide.Parcel.Application.Features.Reliability.ApprovalRequests;

public sealed class ListParcelApprovalRequestsQueryValidator
    : AbstractValidator<ListParcelApprovalRequestsQuery>
{
    public ListParcelApprovalRequestsQueryValidator()
    {
        RuleFor(query => query.DriverUserId).NotEmpty();
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Type)
            .Must(type => type is null
                || type.Equals("CUSTODY_EXCEPTION", StringComparison.OrdinalIgnoreCase)
                || type.Equals("STOP_DEPARTURE", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Type must be CUSTODY_EXCEPTION or STOP_DEPARTURE.");
        RuleFor(query => query.Status)
            .Must(status => status is null
                || status.Equals("PENDING_APPROVAL", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only PENDING_APPROVAL is supported.");
    }
}
