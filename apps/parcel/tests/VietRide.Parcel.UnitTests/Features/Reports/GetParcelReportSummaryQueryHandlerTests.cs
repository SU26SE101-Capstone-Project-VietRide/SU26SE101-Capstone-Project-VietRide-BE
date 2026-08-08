using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.UnitTests.Features;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Reports;

public sealed class GetParcelReportSummaryQueryHandlerTests
{
    private static readonly Guid OperatorId =
        Guid.Parse("41000000-0000-4000-8000-000000000041");
    private static readonly DateOnly From = new(2026, 7, 1);
    private static readonly DateOnly To = new(2026, 7, 31);

    [Fact]
    public async Task Handle_UsesParcelStatsCountsAndCanonicalPaymentMoney()
    {
        var stats = ParcelStats.Create(OperatorId, new DateOnly(2026, 7, 18));
        Set(stats, nameof(ParcelStats.TotalParcels), 9);
        Set(stats, nameof(ParcelStats.TotalLoaded), 8);
        Set(stats, nameof(ParcelStats.TotalDelivered), 7);
        Set(stats, nameof(ParcelStats.TotalRejected), 1);
        Set(stats, nameof(ParcelStats.TotalReturned), 2);
        Set(stats, nameof(ParcelStats.TotalRevenue), 99_999L);
        Set(stats, nameof(ParcelStats.TotalRefunded), 88_888L);
        var statsRepository = Substitute.For<IParcelStatsRepository>();
        statsRepository.QueryNoTracking().Returns(new[] { stats }.AsAsyncQueryable());
        var payment = Substitute.For<IPaymentOperatorRevenueSummaryClient>();
        payment.GetAsync(OperatorId, From, To, Arg.Any<CancellationToken>())
            .Returns(new PaymentOperatorRevenueSummaryDto(1_000, -250, 750));
        var handler = CreateHandler(statsRepository, payment);

        var result = await handler.Handle(
            new GetParcelReportSummaryQuery(OperatorId, From, To),
            CancellationToken.None);

        result.TotalParcels.Should().Be(9);
        result.TotalLoaded.Should().Be(8);
        result.TotalDelivered.Should().Be(7);
        result.TotalRejected.Should().Be(1);
        result.TotalReturned.Should().Be(2);
        result.GrossParcelRevenueVnd.Should().Be(1_000);
        result.ParcelRefundsVnd.Should().Be(-250);
        result.NetParcelRevenueVnd.Should().Be(750);
        result.Source.Should().Be("ParcelStats");
    }

    [Fact]
    public async Task Handle_WhenPaymentUnavailable_Throws503WithoutStatsMoneyFallback()
    {
        var stats = ParcelStats.Create(OperatorId, new DateOnly(2026, 7, 18));
        Set(stats, nameof(ParcelStats.TotalRevenue), 99_999L);
        Set(stats, nameof(ParcelStats.TotalRefunded), 88_888L);
        var statsRepository = Substitute.For<IParcelStatsRepository>();
        statsRepository.QueryNoTracking().Returns(new[] { stats }.AsAsyncQueryable());
        var payment = Substitute.For<IPaymentOperatorRevenueSummaryClient>();
        payment.GetAsync(OperatorId, From, To, Arg.Any<CancellationToken>())
            .Returns<Task<PaymentOperatorRevenueSummaryDto>>(_ => throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Payment unavailable."));
        var handler = CreateHandler(statsRepository, payment);

        var act = () => handler.Handle(
            new GetParcelReportSummaryQuery(OperatorId, From, To),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.StatusCode.Should().Be(503);
    }

    private static GetParcelReportSummaryQueryHandler CreateHandler(
        IParcelStatsRepository statsRepository,
        IPaymentOperatorRevenueSummaryClient payment)
        => new(
            statsRepository,
            Substitute.For<IParcelRepository>(),
            payment,
            new FixedClock());

    private static void Set<T>(ParcelStats stats, string propertyName, T value)
        => typeof(ParcelStats).GetProperty(propertyName)!.SetValue(stats, value);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}
