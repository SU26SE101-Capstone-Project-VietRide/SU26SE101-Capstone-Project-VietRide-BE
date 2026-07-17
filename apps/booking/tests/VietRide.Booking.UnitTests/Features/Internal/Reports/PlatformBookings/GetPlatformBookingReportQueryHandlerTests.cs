using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.UnitTests.Features.Internal.Reports.PlatformBookings;

public sealed class GetPlatformBookingReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithValidUtcRange_ReturnsRepositoryRowsAndUsesHalfOpenBounds()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        IReadOnlyList<PlatformBookingReportItem> rows =
        [
            new(Guid.Parse("40000000-0000-0000-0000-000000000001"), 2, 350_000),
        ];
        var repository = Substitute.For<IBookingRepository>();
        repository.GetPlatformBookingMetricsAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(rows);
        var handler = new GetPlatformBookingReportQueryHandler(repository);

        var result = await handler.Handle(
            new GetPlatformBookingReportQuery(
                "2026-01-01T00:00:00.0000000Z",
                "2026-12-31T23:59:59Z"),
            CancellationToken.None);

        result.Items.Should().BeEquivalentTo(rows);
        await repository.Received(1).GetPlatformBookingMetricsAsync(
            from,
            to,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-01T00:00:00Z", null)]
    [InlineData("2026-01-01T00:00:00+00:00", "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-01T00:00:00z", "2026-01-02T00:00:00Z")]
    [InlineData("not-a-time", "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-02T00:00:00Z", "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-03T00:00:00Z", "2026-01-02T00:00:00Z")]
    [InlineData("2025-01-01T00:00:00Z", "2026-01-03T00:00:00Z")]
    public async Task Handle_WithInvalidRange_ThrowsCanonicalValidationError(string? from, string? to)
    {
        var repository = Substitute.For<IBookingRepository>();
        var handler = new GetPlatformBookingReportQueryHandler(repository);

        var act = () => handler.Handle(
            new GetPlatformBookingReportQuery(from, to),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await repository.DidNotReceiveWithAnyArgs().GetPlatformBookingMetricsAsync(
            default,
            default,
            default);
    }

    [Fact]
    public async Task Handle_WhenRepositoryDetectsOverflow_PropagatesCanonicalException()
    {
        var repository = Substitute.For<IBookingRepository>();
        repository.GetPlatformBookingMetricsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PlatformBookingReportItem>>>(
                _ => throw new PlatformReportValueOverflowException());
        var handler = new GetPlatformBookingReportQueryHandler(repository);

        var act = () => handler.Handle(
            new GetPlatformBookingReportQuery(
                "2026-01-01T00:00:00Z",
                "2026-01-02T00:00:00Z"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PlatformReportValueOverflowException>();
        exception.Which.ErrorCode.Should().Be("REPORT_VALUE_OVERFLOW");
        exception.Which.StatusCode.Should().Be(500);
    }
}
