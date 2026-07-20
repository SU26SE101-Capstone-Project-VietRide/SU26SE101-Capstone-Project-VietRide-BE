using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Shared.Reporting.UnitTests;

public sealed class OperatorReportRangeTests
{
    [Fact]
    public void Create_DefaultRange_UsesThirtyInclusiveIctDays()
    {
        var result = OperatorReportRange.Create(null, null, new FixedClock());

        result.FromDate.Should().Be(new DateOnly(2026, 6, 19));
        result.ToDate.Should().Be(new DateOnly(2026, 7, 18));
        result.FromUtc.Should().Be(new DateTimeOffset(2026, 6, 18, 17, 0, 0, TimeSpan.Zero));
        result.ToUtc.Should().Be(new DateTimeOffset(2026, 7, 18, 17, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [MemberData(nameof(InvalidRanges))]
    public void Create_InvalidOrOverflowingRange_UsesCanonicalError(DateOnly? from, DateOnly? to)
    {
        var action = () => OperatorReportRange.Create(from, to, new FixedClock());

        action.Should().Throw<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "REPORT_RANGE_INVALID");
    }

    public static TheoryData<DateOnly?, DateOnly?> InvalidRanges()
        => new()
        {
            { new DateOnly(2026, 7, 19), new DateOnly(2026, 7, 18) },
            { new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 18) },
            { DateOnly.MaxValue, DateOnly.MaxValue },
            { null, DateOnly.MinValue },
        };

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}
