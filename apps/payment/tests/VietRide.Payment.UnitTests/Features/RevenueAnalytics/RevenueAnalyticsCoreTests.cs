using FluentAssertions;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.UnitTests.Features.RevenueAnalytics;

public sealed class RevenueAnalyticsCoreTests
{
    [Fact]
    public void AdminRange_UsesInclusiveIctBoundariesAndEqualPreviousPeriod()
    {
        var range = RevenueAnalyticsPeriodRules.AdminRange(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        range.FromUtc.Should().Be(DateTimeOffset.Parse("2026-06-30T17:00:00Z"));
        range.ToUtc.Should().Be(DateTimeOffset.Parse("2026-07-31T17:00:00Z"));
        range.PreviousFromUtc.Should().Be(DateTimeOffset.Parse("2026-05-30T17:00:00Z"));
        range.PreviousToUtc.Should().Be(DateTimeOffset.Parse("2026-06-30T17:00:00Z"));
    }

    [Theory]
    [InlineData("2026-7")]
    [InlineData("2026-00")]
    [InlineData("2026-13")]
    [InlineData("")]
    public void OperatorMonth_RejectsInvalidStrictMonth(string month)
    {
        var act = () => RevenueAnalyticsPeriodRules.OperatorMonth(month);

        act.Should().Throw<CodedValidationException>()
            .Which.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public void OperatorMonth_ReturnsTwelveMonthsAndPreviousIctRange()
    {
        var period = RevenueAnalyticsPeriodRules.OperatorMonth("2026-07");

        period.Months.Should().HaveCount(12);
        period.Months.First().Should().Be("2025-08");
        period.Months.Last().Should().Be("2026-07");
        period.CurrentFromUtc.Should().Be(DateTimeOffset.Parse("2026-06-30T17:00:00Z"));
        period.CurrentToUtc.Should().Be(DateTimeOffset.Parse("2026-07-31T17:00:00Z"));
        period.PreviousFromUtc.Should().Be(DateTimeOffset.Parse("2026-05-31T17:00:00Z"));
        period.PreviousToUtc.Should().Be(DateTimeOffset.Parse("2026-06-30T17:00:00Z"));
        period.QueryFromUtc.Should().Be(DateTimeOffset.Parse("2025-07-31T17:00:00Z"));
    }

    [Fact]
    public void AdminRange_RejectsMissingReversedOversizedAndUnrepresentablePrevious()
    {
        Action[] actions =
        [
            () => RevenueAnalyticsPeriodRules.AdminRange(null, new DateOnly(2026, 1, 1)),
            () => RevenueAnalyticsPeriodRules.AdminRange(new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1)),
            () => RevenueAnalyticsPeriodRules.AdminRange(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2)),
            () => RevenueAnalyticsPeriodRules.AdminRange(DateOnly.MinValue, DateOnly.MinValue),
        ];

        actions.Should().AllSatisfy(action => action.Should().Throw<CodedValidationException>());
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(7, 7)]
    [InlineData(99, 20)]
    public void ClampTop_UsesContractBounds(int? top, int expected)
        => RevenueAnalyticsPeriodRules.ClampTop(top).Should().Be(expected);

    [Theory]
    [InlineData(100, 0, null, "UP")]
    [InlineData(0, 0, "0", "FLAT")]
    [InlineData(-10, 0, null, "DOWN")]
    [InlineData(113, 100, "13", "UP")]
    [InlineData(100, 113, "-11.5", "DOWN")]
    [InlineData(20201, 20000, "1.01", "UP")]
    [InlineData(19799, 20000, "-1.01", "DOWN")]
    public void Comparison_HandlesZeroDenominatorTrendAndAwayFromZeroRounding(
        long current,
        long previous,
        string? expectedPercent,
        string expectedTrend)
    {
        var result = RevenueComparisonFactory.Create(current, previous);

        result.ChangePercent.Should().Be(expectedPercent is null
            ? null
            : decimal.Parse(expectedPercent, System.Globalization.CultureInfo.InvariantCulture));
        result.Trend.Should().Be(expectedTrend);
    }

    [Fact]
    public void OperatorYear_ReturnsCalendarMonthsAndPreviousYearRange()
    {
        var period = RevenueAnalyticsPeriodRules.OperatorPeriod(null, 2026, "month");

        period.IsYearMode.Should().BeTrue();
        period.Months.Should().Equal(
            "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06",
            "2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12");
        period.CurrentFromUtc.Should().Be(DateTimeOffset.Parse("2025-12-31T17:00:00Z"));
        period.CurrentToUtc.Should().Be(DateTimeOffset.Parse("2026-12-31T17:00:00Z"));
        period.PreviousFromUtc.Should().Be(DateTimeOffset.Parse("2024-12-31T17:00:00Z"));
        period.PreviousToUtc.Should().Be(DateTimeOffset.Parse("2025-12-31T17:00:00Z"));
        period.QueryFromUtc.Should().Be(period.PreviousFromUtc);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("2026-07", 2026, "month")]
    [InlineData("2026-07", null, "month")]
    [InlineData(null, 2026, null)]
    [InlineData(null, 2026, "day")]
    public void OperatorPeriod_RejectsAnythingExceptMonthOrYearContract(
        string? month,
        int? year,
        string? groupBy)
    {
        var act = () => RevenueAnalyticsPeriodRules.OperatorPeriod(month, year, groupBy);

        act.Should().Throw<CodedValidationException>();
    }
}
