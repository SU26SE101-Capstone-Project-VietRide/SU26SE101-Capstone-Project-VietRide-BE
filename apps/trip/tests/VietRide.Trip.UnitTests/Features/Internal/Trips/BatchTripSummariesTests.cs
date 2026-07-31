using FluentAssertions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class BatchTripSummariesTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void BatchTripSummaries_Validator_AcceptsBoundaryCounts(int count)
    {
        var query = new BatchTripSummariesQuery(
            Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray());

        var result = new BatchTripSummariesQueryValidator().Validate(query);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void BatchTripSummaries_Validator_RejectsOutOfRangeCounts(int count)
    {
        var query = new BatchTripSummariesQuery(
            Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray());

        var result = new BatchTripSummariesQueryValidator().Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "TripIds");
    }

    [Fact]
    public void BatchTripSummaries_Validator_RejectsEmptyAndDuplicateIds()
    {
        var duplicated = Guid.NewGuid();
        var query = new BatchTripSummariesQuery([Guid.Empty, duplicated, duplicated]);

        var result = new BatchTripSummariesQueryValidator().Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "TripIds");
    }

    [Fact]
    public async Task BatchTripSummaries_Handler_UsesOneRepositoryCallAndPreservesResult()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var expected = new[]
        {
            new InternalTripSummaryDto(
                ids[0],
                "IN_PROGRESS",
                DateTimeOffset.Parse("2026-07-29T01:00:00Z"),
                DateTimeOffset.Parse("2026-07-29T08:00:00Z"),
                new InternalTripRouteSummaryDto(Guid.NewGuid(), "HCM - Da Lat", "HCM", "Da Lat"),
                new InternalTripVehicleSummaryDto(Guid.NewGuid(), "51B-123.45", "MAINTENANCE"),
                Guid.NewGuid(),
                null),
        };
        var repository = new FakeTripRepository(expected);
        using var cancellation = new CancellationTokenSource();
        var handler = new BatchTripSummariesQueryHandler(repository);

        var result = await handler.Handle(new BatchTripSummariesQuery(ids), cancellation.Token);

        result.Should().BeSameAs(expected);
        repository.CallCount.Should().Be(1);
        repository.ReceivedIds.Should().Equal(ids);
        repository.ReceivedCancellationToken.Should().Be(cancellation.Token);
    }

    private sealed class FakeTripRepository(IReadOnlyList<InternalTripSummaryDto> result) : ITripRepository
    {
        public int CallCount { get; private set; }
        public IReadOnlyCollection<Guid> ReceivedIds { get; private set; } = [];
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<IReadOnlyList<InternalTripSummaryDto>> ListSummariesByIdsAsync(
            IReadOnlyCollection<Guid> tripIds,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedIds = tripIds;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(result);
        }

        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct) => throw new NotSupportedException();
        public void Update(TripEntity entity) => throw new NotSupportedException();
        public void Remove(TripEntity entity) => throw new NotSupportedException();
        public IQueryable<TripEntity> Query() => throw new NotSupportedException();
        public IQueryable<TripEntity> QueryNoTracking() => throw new NotSupportedException();
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
