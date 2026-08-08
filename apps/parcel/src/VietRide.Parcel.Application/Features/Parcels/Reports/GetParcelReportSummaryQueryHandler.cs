using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed class GetParcelReportSummaryQueryHandler
    : IRequestHandler<GetParcelReportSummaryQuery, ParcelReportSummaryResponse>
{
    private readonly IParcelStatsRepository statsRepository;
    private readonly IParcelRepository parcelRepository;
    private readonly IPaymentOperatorRevenueSummaryClient paymentRevenue;
    private readonly IClock clock;

    public GetParcelReportSummaryQueryHandler(
        IParcelStatsRepository statsRepository,
        IParcelRepository parcelRepository,
        IPaymentOperatorRevenueSummaryClient paymentRevenue,
        IClock clock)
    {
        this.statsRepository = statsRepository;
        this.parcelRepository = parcelRepository;
        this.paymentRevenue = paymentRevenue;
        this.clock = clock;
    }

    public async Task<ParcelReportSummaryResponse> Handle(
        GetParcelReportSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ParcelReportQuerySupport.NormalizeRange(request.From, request.To, clock);
        return await ParcelReportQuerySupport.BuildSummaryAsync(
            statsRepository,
            parcelRepository,
            paymentRevenue,
            request.OperatorId,
            from,
            to,
            cancellationToken);
    }
}
