using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.UnitTests.Features;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Reports;

public sealed class ExportParcelReportQueryHandlerCompatibilityTests
{
    [Fact]
    public async Task Handle_Csv_PreservesLegacyFilenameMimeHeaderAndRowLayout()
    {
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000041");
        var stats = ParcelStats.Create(operatorId, new DateOnly(2026, 7, 18));
        var statsRepository = Substitute.For<IParcelStatsRepository>();
        statsRepository.QueryNoTracking().Returns(new[] { stats }.AsAsyncQueryable());
        var handler = new ExportParcelReportQueryHandler(
            statsRepository,
            Substitute.For<IParcelRepository>(),
            new FixedClock());

        var result = await handler.Handle(
            new ExportParcelReportQuery(
                operatorId,
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 18),
                "csv"),
            CancellationToken.None);

        const string header = "operatorId,from,to,totalParcels,totalLoaded,totalDelivered,totalRejected,totalReturned,totalRevenue,totalRefunded,source";
        var row = $"{operatorId:D},2026-07-18,2026-07-18,0,0,0,0,0,0,0,ParcelStats";
        result.FileName.Should().Be("parcel-report-20260718-20260718.csv");
        result.ContentType.Should().Be("text/csv");
        result.Content.Should().Be($"{header}{Environment.NewLine}{row}{Environment.NewLine}");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}
