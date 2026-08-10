using FluentAssertions;
using VietRide.Shared.Kernel.Time;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Time;

public sealed class BusinessTimeTests
{
    [Theory]
    [InlineData(17, 10)]
    [InlineData(18, 11)]
    [InlineData(19, 12)]
    public void ToUtc_ConvertsVietnamBusinessTimeToUtc(int localHour, int utcHour)
    {
        var result = BusinessTime.ToUtc(
            new DateOnly(2026, 8, 10),
            new TimeOnly(localHour, 0));

        result.Should().Be(new DateTimeOffset(2026, 8, 10, utcHour, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetUtcDayRange_UsesHalfOpenVietnamCalendarDay()
    {
        var result = BusinessTime.GetUtcDayRange(new DateOnly(2026, 8, 10));

        result.FromUtc.Should().Be(new DateTimeOffset(2026, 8, 9, 17, 0, 0, TimeSpan.Zero));
        result.ToUtcExclusive.Should().Be(new DateTimeOffset(2026, 8, 10, 17, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0, 0, 0, 2026, 8, 9, 17, 0, 0)]
    [InlineData(0, 30, 0, 2026, 8, 9, 17, 30, 0)]
    [InlineData(23, 59, 59, 2026, 8, 10, 16, 59, 59)]
    public void ToUtc_HandlesVietnamDayBoundaries(
        int localHour,
        int localMinute,
        int localSecond,
        int utcYear,
        int utcMonth,
        int utcDay,
        int utcHour,
        int utcMinute,
        int utcSecond)
    {
        var result = BusinessTime.ToUtc(
            new DateOnly(2026, 8, 10),
            new TimeOnly(localHour, localMinute, localSecond));

        result.Should().Be(new DateTimeOffset(
            utcYear,
            utcMonth,
            utcDay,
            utcHour,
            utcMinute,
            utcSecond,
            TimeSpan.Zero));
    }

    [Fact]
    public void ToUtc_PreservesTheLastMillisecondOfVietnamCalendarDay()
    {
        var result = BusinessTime.ToUtc(
            new DateOnly(2026, 8, 10),
            new TimeOnly(23, 59, 59, 999));

        result.Should().Be(
            new DateTimeOffset(2026, 8, 10, 16, 59, 59, 999, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(2024, 2, 29, 2024, 3, 1)]
    [InlineData(2026, 12, 31, 2027, 1, 1)]
    public void GetUtcDayRange_CoversLeapAndYearBoundaries(
        int year,
        int month,
        int day,
        int nextYear,
        int nextMonth,
        int nextDay)
    {
        var result = BusinessTime.GetUtcDayRange(new DateOnly(year, month, day));

        BusinessTime.ToLocalDate(result.FromUtc).Should().Be(new DateOnly(year, month, day));
        BusinessTime.ToLocalDate(result.ToUtcExclusive).Should().Be(
            new DateOnly(nextYear, nextMonth, nextDay));
        (result.ToUtcExclusive - result.FromUtc).Should().Be(TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData(2026, 8, 10, 1)]
    [InlineData(2026, 8, 9, 7)]
    public void ToIsoDayOfWeek_MapsMondayThroughSundayToOneThroughSeven(
        int year,
        int month,
        int day,
        int expected)
    {
        BusinessTime.ToIsoDayOfWeek(new DateOnly(year, month, day)).Should().Be(expected);
    }
}
