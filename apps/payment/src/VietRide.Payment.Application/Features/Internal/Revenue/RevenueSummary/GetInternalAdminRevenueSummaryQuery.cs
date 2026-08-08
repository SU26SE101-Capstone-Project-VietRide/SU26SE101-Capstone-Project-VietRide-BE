using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;

public sealed record GetInternalAdminRevenueSummaryQuery(string? From, string? To)
    : IRequest<InternalAdminRevenueSummaryResult>;
