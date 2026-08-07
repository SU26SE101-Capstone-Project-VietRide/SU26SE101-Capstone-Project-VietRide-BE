using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;

public sealed record GetInternalOperatorRevenueSummaryQuery(
    Guid OperatorId,
    string? From,
    string? To) : IRequest<InternalOperatorRevenueSummaryResult>;
