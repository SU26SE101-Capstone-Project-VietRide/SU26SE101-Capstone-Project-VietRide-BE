using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Incidents;

public sealed class OperatorIncidentQueryHandlerTests
{
    [Fact]
    public async Task List_UsesTenantFiltersStablePagingAndOneReporterBatch()
    {
        var operatorId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var row = CreateRow(reporterId);
        var repository = new RecordingIncidentRepository([row]);
        var identity = new RecordingIdentityClient(new Dictionary<Guid, IdentityUserProfile>
        {
            [reporterId] = new(reporterId, "Driver A", null, "DRIVER", operatorId, "ACTIVE"),
        });
        var handler = new ListOperatorIncidentsHandler(repository, identity);

        var result = await handler.Handle(
            new ListOperatorIncidentsQuery(
                operatorId,
                row.TripId,
                "ACCIDENT",
                "OPEN",
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 2),
                null,
                null),
            CancellationToken.None);

        repository.ListCall.Should().NotBeNull();
        repository.ListCall!.OperatorId.Should().Be(operatorId);
        repository.ListCall.TripId.Should().Be(row.TripId);
        repository.ListCall.Category.Should().Be(IncidentCategory.ACCIDENT);
        repository.ListCall.Resolved.Should().BeFalse();
        repository.ListCall.Page.Should().Be(1);
        repository.ListCall.PageSize.Should().Be(20);
        repository.ListCall.FromUtc.Should().Be(DateTimeOffset.Parse("2026-07-31T17:00:00Z"));
        repository.ListCall.ToUtcExclusive.Should().Be(DateTimeOffset.Parse("2026-08-02T17:00:00Z"));
        identity.Batches.Should().ContainSingle().Which.Should().Equal(reporterId);
        var item = result.Items.Should().ContainSingle().Which;
        item.Status.Should().Be("OPEN");
        item.Reporter.Should().Be(new OperatorIncidentReporterDto(reporterId, "Driver A", "DRIVER"));
        item.Trip.Route.OriginStation.StationId.Should().Be(row.OriginStationId);
    }

    [Fact]
    public async Task List_WhenIdentityProfileIsMissing_ReturnsNullableReporterFields()
    {
        var row = CreateRow(Guid.NewGuid());
        var handler = new ListOperatorIncidentsHandler(
            new RecordingIncidentRepository([row]),
            new RecordingIdentityClient(new Dictionary<Guid, IdentityUserProfile>()));

        var result = await handler.Handle(
            new ListOperatorIncidentsQuery(Guid.NewGuid(), null, null, null, null, null, 2, 100),
            CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Reporter.Should().Be(
            new OperatorIncidentReporterDto(row.ReportedByUserId, null, null));
    }

    [Fact]
    public async Task Detail_WhenIncidentIsMissingOrForeign_ThrowsIncidentNotFound()
    {
        var handler = new GetOperatorIncidentHandler(
            new RecordingIncidentRepository([]),
            new RecordingIdentityClient(new Dictionary<Guid, IdentityUserProfile>()));

        var action = () => handler.Handle(
            new GetOperatorIncidentQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("INCIDENT_NOT_FOUND");
    }

    [Fact]
    public void Validator_RejectsInvalidRangeAndPageBounds()
    {
        var result = new ListOperatorIncidentsValidator().Validate(
            new ListOperatorIncidentsQuery(
                Guid.NewGuid(),
                null,
                null,
                null,
                new DateOnly(2026, 8, 2),
                new DateOnly(2026, 8, 1),
                0,
                101));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData(null, "0")]
    [InlineData("TRAFFIC_JAM,OTHER", null)]
    [InlineData(null, "OPEN,RESOLVED")]
    public void Validator_RejectsNumericEnumFilters(string? category, string? status)
    {
        var result = new ListOperatorIncidentsValidator().Validate(
            new ListOperatorIncidentsQuery(
                Guid.NewGuid(),
                null,
                category,
                status,
                null,
                null,
                1,
                20));

        result.IsValid.Should().BeFalse();
    }

    private static OperatorIncidentReadRow CreateRow(Guid reporterId)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            IncidentCategory.ACCIDENT,
            "Minor collision",
            ["https://storage.example/incident.jpg"],
            10.75m,
            106.67m,
            DateTimeOffset.Parse("2026-08-01T03:00:00Z"),
                null,
                null,
            null,
            reporterId,
            TripStatus.IN_PROGRESS,
            DateTimeOffset.Parse("2026-08-01T01:00:00Z"),
            Guid.NewGuid(),
            "HCM - Da Lat",
            Guid.NewGuid(),
            "Mien Dong",
            Guid.NewGuid(),
            "Da Lat");

    private sealed class RecordingIncidentRepository(IReadOnlyList<OperatorIncidentReadRow> rows)
        : IIncidentRepository
    {
        public ListCall? ListCall { get; private set; }

        public Task<PagedResult<OperatorIncidentReadRow>> ListOperatorIncidentsAsync(
            Guid operatorId,
            Guid? tripId,
            IncidentCategory? category,
            bool? resolved,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtcExclusive,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ListCall = new(operatorId, tripId, category, resolved, fromUtc, toUtcExclusive, page, pageSize);
            return Task.FromResult(PagedResult<OperatorIncidentReadRow>.Create(rows, page, pageSize, rows.Count));
        }

        public Task<OperatorIncidentReadRow?> GetOperatorIncidentAsync(
            Guid operatorId,
            Guid incidentId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(rows.SingleOrDefault(row => row.IncidentId == incidentId));

        public Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Incident?>(null);

        public Task<Incident> AddAsync(Incident entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);

        public void Update(Incident entity) { }
        public void Remove(Incident entity) { }
        public IQueryable<Incident> Query() => Array.Empty<Incident>().AsQueryable();
        public IQueryable<Incident> QueryNoTracking() => Query();
    }

    private sealed class RecordingIdentityClient(IReadOnlyDictionary<Guid, IdentityUserProfile> profiles)
        : IIdentityInternalClient
    {
        public List<IReadOnlyCollection<Guid>> Batches { get; } = [];

        public Task<IReadOnlyDictionary<Guid, IdentityUserProfile>> GetUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(userIds);
            return Task.FromResult(profiles);
        }

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityUserLookupResult.ValidationFailure("Not used."));
    }

    private sealed record ListCall(
        Guid OperatorId,
        Guid? TripId,
        IncidentCategory? Category,
        bool? Resolved,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtcExclusive,
        int Page,
        int PageSize);
}
