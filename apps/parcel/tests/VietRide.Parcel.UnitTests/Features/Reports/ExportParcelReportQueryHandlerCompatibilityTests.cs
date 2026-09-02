using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.UnitTests.Features;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Reports;

public sealed class ExportParcelReportQueryHandlerCompatibilityTests
{
    [Fact]
    public async Task Handle_Csv_UsesVietnameseExcelSafeContract()
    {
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000041");
        var stats = ParcelStats.Create(operatorId, new DateOnly(2026, 7, 18));
        var statsRepository = Substitute.For<IParcelStatsRepository>();
        statsRepository.QueryNoTracking().Returns(new[] { stats }.AsAsyncQueryable());
        var paymentRevenue = Substitute.For<IPaymentOperatorRevenueSummaryClient>();
        paymentRevenue.GetAsync(
                operatorId,
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 18),
                Arg.Any<CancellationToken>())
            .Returns(new PaymentOperatorRevenueSummaryDto(700, -200, 500));
        var handler = new ExportParcelReportQueryHandler(
            statsRepository,
            Substitute.For<IParcelRepository>(),
            paymentRevenue,
            new FixedClock());

        var result = await handler.Handle(
            new ExportParcelReportQuery(
                operatorId,
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 18),
                "csv"),
            CancellationToken.None);

        const string header = "Từ ngày,Đến ngày,Tổng bưu kiện,Đã xếp lên xe,Đã giao,Bị từ chối,Đã hoàn trả,Doanh thu gộp,Tiền hoàn,Doanh thu thuần,Mã hệ thống nhà xe";
        var row = $"18/07/2026,18/07/2026,0,0,0,0,0,700,'-200,500,{operatorId:D}";
        result.FileName.Should().Be("bao-cao-tong-hop-buu-kien-20260718-20260718.csv");
        result.ContentType.Should().Be("text/csv; charset=utf-8");
        result.Content.Should().Be($"\uFEFF{header}{Environment.NewLine}{row}{Environment.NewLine}");
        result.Content.Should().NotContain("source");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}
