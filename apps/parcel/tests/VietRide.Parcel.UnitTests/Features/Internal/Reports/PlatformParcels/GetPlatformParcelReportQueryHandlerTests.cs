using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.UnitTests.Features.Internal.Reports.PlatformParcels;

public sealed class GetPlatformParcelReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithValidUtcRange_ReturnsRepositoryRows()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        IReadOnlyList<PlatformParcelReportItem> rows =
        [
            new(Guid.Parse("40000000-0000-0000-0000-000000000001"), 3, -50_000),
        ];
        var repository = Substitute.For<IParcelRepository>();
        repository.GetPlatformParcelMetricsAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(rows);
        var handler = new GetPlatformParcelReportQueryHandler(repository);

        var result = await handler.Handle(
            new GetPlatformParcelReportQuery(
                "2026-01-01T00:00:00.0000000Z",
                "2026-12-31T23:59:59Z"),
            CancellationToken.None);

        result.Items.Should().BeEquivalentTo(rows);
        await repository.Received(1).GetPlatformParcelMetricsAsync(
            from,
            to,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-01T00:00:00Z", null)]
    [InlineData("2026-01-01T00:00:00+00:00", "2026-01-02T00:00:00Z")]
    [InlineData("not-a-time", "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-02T00:00:00Z", "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-03T00:00:00Z", "2026-01-02T00:00:00Z")]
    [InlineData("2025-01-01T00:00:00Z", "2026-01-03T00:00:00Z")]
    public async Task Handle_WithInvalidRange_ThrowsCanonicalValidationError(string? from, string? to)
    {
        var repository = Substitute.For<IParcelRepository>();
        var handler = new GetPlatformParcelReportQueryHandler(repository);

        var act = () => handler.Handle(
            new GetPlatformParcelReportQuery(from, to),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await repository.DidNotReceiveWithAnyArgs().GetPlatformParcelMetricsAsync(
            default,
            default,
            default);
    }

    [Fact]
    public async Task Handle_WhenRepositoryDetectsOverflow_PropagatesCanonicalException()
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.GetPlatformParcelMetricsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PlatformParcelReportItem>>>(
                _ => throw new PlatformReportValueOverflowException(new OverflowException()));
        var handler = new GetPlatformParcelReportQueryHandler(repository);

        var act = () => handler.Handle(
            new GetPlatformParcelReportQuery(
                "2026-01-01T00:00:00Z",
                "2026-01-02T00:00:00Z"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PlatformReportValueOverflowException>();
        exception.Which.ErrorCode.Should().Be("REPORT_VALUE_OVERFLOW");
        exception.Which.StatusCode.Should().Be(500);
    }
}
