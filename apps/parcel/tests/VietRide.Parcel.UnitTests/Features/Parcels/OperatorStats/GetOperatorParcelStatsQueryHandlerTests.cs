using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.OperatorStats;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.UnitTests.Features.Parcels.OperatorStats;

public sealed class GetOperatorParcelStatsQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly IOperatorParcelStatsRepository _repository = Substitute.For<IOperatorParcelStatsRepository>();

    [Fact]
    public async Task OperatorParcelStats_Status_UsesInclusiveIctRangeAndMapsExactShape()
    {
        _repository.GetAsync(
                OperatorId,
                DateTimeOffset.Parse("2026-01-31T17:00:00Z"),
                DateTimeOffset.Parse("2026-02-01T17:00:00Z"),
                "status",
                10,
                Arg.Any<CancellationToken>())
            .Returns(new OperatorParcelStatsReadResult(
                TotalParcels: 3,
                [
                    new OperatorParcelStatsBucketReadModel("IN_TRANSIT", null, null, 2),
                    new OperatorParcelStatsBucketReadModel("CANCELLED", null, null, 1),
                ]));
        var handler = new GetOperatorParcelStatsQueryHandler(_repository);

        var result = await handler.Handle(
            new GetOperatorParcelStatsQuery(
                OperatorId,
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 2, 1),
                "STATUS",
                Limit: null),
            CancellationToken.None);

        result.TotalParcels.Should().Be(3);
        result.Items.Select(item => (item.Key, item.Count)).Should().Equal(
            ("IN_TRANSIT", 2L),
            ("CANCELLED", 1L));
        result.Items.Should().OnlyContain(item => item.RouteId == null && item.RouteName == null && item.ParcelCount == null);
    }

    [Fact]
    public async Task OperatorParcelStats_Route_UsesSnapshotProjectionAndClampsLimit()
    {
        var routeId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        _repository.GetAsync(
                OperatorId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                "route",
                100,
                Arg.Any<CancellationToken>())
            .Returns(new OperatorParcelStatsReadResult(
                TotalParcels: 2,
                [new OperatorParcelStatsBucketReadModel(null, routeId, "Tuyến lịch sử", 2)]));
        var handler = new GetOperatorParcelStatsQueryHandler(_repository);

        var result = await handler.Handle(
            new GetOperatorParcelStatsQuery(
                OperatorId,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31),
                "route",
                Limit: 999),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].RouteId.Should().Be(routeId);
        result.Items[0].RouteName.Should().Be("Tuyến lịch sử");
        result.Items[0].ParcelCount.Should().Be(2);
        result.Items[0].Key.Should().BeNull();
        result.Items[0].Count.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "2026-01-31", "status")]
    [InlineData("2026-01-01", null, "status")]
    [InlineData("2026-02-01", "2026-01-31", "status")]
    [InlineData("2025-01-01", "2026-01-02", "status")]
    [InlineData("2026-01-01", "2026-01-31", "trip")]
    public async Task OperatorParcelStats_InvalidRangeOrGroup_ThrowsValidationBeforeRepository(
        string? fromValue,
        string? toValue,
        string groupBy)
    {
        var handler = new GetOperatorParcelStatsQueryHandler(_repository);
        DateOnly? from = fromValue is null ? null : DateOnly.Parse(fromValue);
        DateOnly? to = toValue is null ? null : DateOnly.Parse(toValue);

        var act = () => handler.Handle(
            new GetOperatorParcelStatsQuery(OperatorId, from, to, groupBy, Limit: null),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(
            default,
            default,
            default,
            default!,
            default,
            default);
    }
}
