using MediatR;

namespace VietRide.Identity.Application.Features.Admin.GetOperatorSummary;

public sealed record GetOperatorSummaryQuery(string CallerRole) : IRequest<AdminOperatorSummaryDto>;
