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

        var csv = new StringBuilder("\uFEFF");
        csv.AppendLine("Từ ngày,Đến ngày,Tổng bưu kiện,Đã xếp lên xe,Đã giao,Bị từ chối,Đã hoàn trả,Doanh thu gộp,Tiền hoàn,Doanh thu thuần,Mã hệ thống nhà xe");
        csv.AppendLine(string.Join(',', new[]
        {
            Escape(summary.From.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
            Escape(summary.To.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
            summary.TotalParcels.ToString(CultureInfo.InvariantCulture),
            summary.TotalLoaded.ToString(CultureInfo.InvariantCulture),
            summary.TotalDelivered.ToString(CultureInfo.InvariantCulture),
            summary.TotalRejected.ToString(CultureInfo.InvariantCulture),
            summary.TotalReturned.ToString(CultureInfo.InvariantCulture),
            summary.GrossParcelRevenueVnd.ToString(CultureInfo.InvariantCulture),
            Escape(summary.ParcelRefundsVnd.ToString(CultureInfo.InvariantCulture)),
            summary.NetParcelRevenueVnd.ToString(CultureInfo.InvariantCulture),
            Escape(summary.OperatorId.ToString("D")),
        }));

        var fileName = $"bao-cao-tong-hop-buu-kien-{from:yyyyMMdd}-{to:yyyyMMdd}.csv";
        return new ParcelReportExportResponse(fileName, "text/csv; charset=utf-8", csv.ToString());
    }

    private static string Escape(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{safe.Replace("\"", "\"\"")}\""
            : safe;
    }
}
