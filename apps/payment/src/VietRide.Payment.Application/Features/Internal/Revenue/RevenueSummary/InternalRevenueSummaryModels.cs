namespace VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;

public sealed record InternalRevenuePeriod(DateOnly From, DateOnly To, string Timezone);

public sealed record InternalAdminRevenueSummaryResult(
    InternalRevenuePeriod Period,
    long TotalProjectRevenueVnd,
    long NetTransportRevenueVnd,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long SubscriptionRevenueVnd,
    long PaidToOperatorsVnd,
    DateTime GeneratedAt);

public sealed record InternalOperatorRevenueSummaryResult(
    InternalRevenuePeriod Period,
    Guid OperatorId,
    long NetRevenueVnd,
    long NetTicketRevenueVnd,
    long NetParcelRevenueVnd,
    long GrossParcelRevenueVnd,
    long ParcelRefundsVnd,
    DateTime GeneratedAt);
