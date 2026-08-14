namespace VietRide.Identity.Application.Features.Admin.GetOperatorSummary;

public sealed record AdminOperatorSummaryDto(
    int Total,
    int Pending,
    int Approved,
    int Suspended,
    int Rejected,
    int Active);
