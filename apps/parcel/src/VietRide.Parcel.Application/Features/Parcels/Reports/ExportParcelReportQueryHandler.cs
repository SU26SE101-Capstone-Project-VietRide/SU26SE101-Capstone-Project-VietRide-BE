using System.Globalization;
using System.Text;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed class ExportParcelReportQueryHandler
    : IRequestHandler<ExportParcelReportQuery, ParcelReportExportResponse>
{
    private readonly IParcelStatsRepository statsRepository;
    private readonly IParcelRepository parcelRepository;
    private readonly IPaymentOperatorRevenueSummaryClient paymentRevenue;
    private readonly IClock clock;

    public ExportParcelReportQueryHandler(
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

    public async Task<ParcelReportExportResponse> Handle(
        ExportParcelReportQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Format)
            && !string.Equals(request.Format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only csv export format is supported.", nameof(request.Format));
        }

        var (from, to) = ParcelReportQuerySupport.NormalizeRange(request.From, request.To, clock);
        var summary = await ParcelReportQuerySupport.BuildSummaryAsync(
            statsRepository,
            parcelRepository,
            paymentRevenue,
            request.OperatorId,
            from,
            to,
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("operatorId,from,to,totalParcels,totalLoaded,totalDelivered,totalRejected,totalReturned,grossParcelRevenueVnd,parcelRefundsVnd,netParcelRevenueVnd,source");
        csv.Append(summary.OperatorId).Append(',')
            .Append(summary.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
            .Append(summary.TotalParcels).Append(',')
            .Append(summary.TotalLoaded).Append(',')
            .Append(summary.TotalDelivered).Append(',')
            .Append(summary.TotalRejected).Append(',')
            .Append(summary.TotalReturned).Append(',')
            .Append(summary.GrossParcelRevenueVnd).Append(',')
            .Append(summary.ParcelRefundsVnd).Append(',')
            .Append(summary.NetParcelRevenueVnd).Append(',')
            .Append(summary.Source).AppendLine();

        var fileName = $"parcel-report-{from:yyyyMMdd}-{to:yyyyMMdd}.csv";
        return new ParcelReportExportResponse(fileName, "text/csv", csv.ToString());
    }
}
