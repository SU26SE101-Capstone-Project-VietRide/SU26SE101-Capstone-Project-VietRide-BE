namespace VietRide.Identity.Application.Features.Admin.CreateOperator;

public sealed record CreateOperatorResponseDto(
    OperatorSummaryDto Operator,
    OperatorAdminSummaryDto AdminUser,
    OperatorSubscriptionSummaryDto Subscription);
