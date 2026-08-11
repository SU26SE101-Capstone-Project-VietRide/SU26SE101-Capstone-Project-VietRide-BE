using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;
using VietRide.Trip.Application.Features.Incidents.ResolveIncident;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Incidents;

public sealed class ResolveIncidentTests
{
    [Fact]
    public async Task Resolve_SetsServerTimestampActorAndTrimmedNote()
    {
        var operatorId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-11T06:00:00Z");
        var incident = CreateIncident(reporterId);
        var row = CreateRow(incident, operatorId, reporterId);
        var repository = new IncidentRepositoryFake(operatorId, incident, row);
        var handler = new ResolveIncidentCommandHandler(
            repository,
            new IdentityClientFake(reporterId, operatorId),
            new UnitOfWorkFake(),
            new ClockFake(now));

        var result = await handler.Handle(
            new ResolveIncidentCommand(operatorId, actorId, incident.Id, "  Detoured safely  "),
            CancellationToken.None);

        incident.ResolvedAt.Should().Be(now);
        incident.ResolvedByUserId.Should().Be(actorId);
        incident.ResolutionNote.Should().Be("Detoured safely");
        result.Status.Should().Be("RESOLVED");
        result.ResolvedAt.Should().Be(now);
        result.ResolvedByUserId.Should().Be(actorId);
        result.ResolutionNote.Should().Be("Detoured safely");
    }

    [Fact]
    public async Task Resolve_MissingOrForeignIncident_IsMaskedAsNotFound()
    {
        var operatorId = Guid.NewGuid();
        var incident = CreateIncident(Guid.NewGuid());
        var handler = new ResolveIncidentCommandHandler(
            new IncidentRepositoryFake(Guid.NewGuid(), incident, null),
            new IdentityClientFake(Guid.NewGuid(), operatorId),
            new UnitOfWorkFake(),
            new ClockFake(DateTimeOffset.UtcNow));

        var action = () => handler.Handle(
            new ResolveIncidentCommand(operatorId, Guid.NewGuid(), incident.Id, "Resolved"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("INCIDENT_NOT_FOUND");
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_ReturnsStableConflict()
    {
        var operatorId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var incident = CreateIncident(reporterId);
        incident.Resolve(Guid.NewGuid(), "Already done", DateTimeOffset.Parse("2026-08-11T05:00:00Z"));
        var row = CreateRow(incident, operatorId, reporterId) with
        {
            ResolvedAt = incident.ResolvedAt,
            ResolvedByUserId = incident.ResolvedByUserId,
            ResolutionNote = incident.ResolutionNote,
        };
        var handler = new ResolveIncidentCommandHandler(
            new IncidentRepositoryFake(operatorId, incident, row),
            new IdentityClientFake(reporterId, operatorId),
            new UnitOfWorkFake(),
            new ClockFake(DateTimeOffset.UtcNow));

        var action = () => handler.Handle(
            new ResolveIncidentCommand(operatorId, Guid.NewGuid(), incident.Id, "Again"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("INCIDENT_ALREADY_RESOLVED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_RejectsBlankResolutionNote(string note)
    {
        var result = new ResolveIncidentCommandValidator().Validate(
            new ResolveIncidentCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), note));

        result.IsValid.Should().BeFalse();
    }

    private static Incident CreateIncident(Guid reporterId)
        => Incident.Create(
            Guid.NewGuid(),
            reporterId,
            IncidentCategory.ACCIDENT,
            "Minor collision",
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-08-11T04:00:00Z"));

    private static OperatorIncidentReadRow CreateRow(Incident incident, Guid operatorId, Guid reporterId)
        => new(
            incident.Id,
            incident.TripId,
            incident.Category,
            incident.Description,
            incident.PhotoUrls,
            incident.Latitude,
            incident.Longitude,
            incident.ReportedAt,
            null,
            null,
            null,
            reporterId,
            TripStatus.IN_PROGRESS,
            DateTimeOffset.Parse("2026-08-11T03:00:00Z"),
            Guid.NewGuid(),
            $"Route {operatorId:D}",
            Guid.NewGuid(),
            "Origin",
            Guid.NewGuid(),
            "Destination");

    private sealed class IncidentRepositoryFake(
        Guid ownerOperatorId,
        Incident incident,
        OperatorIncidentReadRow? row) : IIncidentRepository
    {
        public Task<OperatorIncidentReadRow?> GetOperatorIncidentAsync(
            Guid operatorId,
            Guid incidentId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(operatorId == ownerOperatorId && incidentId == incident.Id ? row : null);

        public Task<Incident?> AcquireOperatorIncidentAsync(
            Guid operatorId,
            Guid incidentId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Incident?>(
                operatorId == ownerOperatorId && incidentId == incident.Id ? incident : null);

        public Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == incident.Id ? incident : null);
        public Task<Incident> AddAsync(Incident entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);
        public void Update(Incident entity) { }
        public void Remove(Incident entity) { }
        public IQueryable<Incident> Query() => new[] { incident }.AsQueryable();
        public IQueryable<Incident> QueryNoTracking() => Query();
    }

    private sealed class IdentityClientFake(Guid reporterId, Guid operatorId) : IIdentityInternalClient
    {
        public Task<IReadOnlyDictionary<Guid, IdentityUserProfile>> GetUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, IdentityUserProfile>>(
                new Dictionary<Guid, IdentityUserProfile>
                {
                    [reporterId] = new(reporterId, "Driver", null, "DRIVER", operatorId, "ACTIVE"),
                });

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityUserLookupResult.ValidationFailure("Not used."));
    }

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ClockFake(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
