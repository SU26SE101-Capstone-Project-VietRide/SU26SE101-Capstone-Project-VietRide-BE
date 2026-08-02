using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Messaging;

namespace VietRide.Identity.UnitTests.Infrastructure;

public sealed class StationAuditEventHandlerTests
{
    [Fact]
    public async Task Merge_MapsActorActionSourceAndAuditColumns_WithoutMetadataOrPiiLogs()
    {
        var activityLogs = CreateActivityLogRepository();
        var logger = Substitute.For<ILogger<StationMergedIntegrationEventHandler>>();
        var integrationEvent = CreateMergedEvent(
            ipAddress: "203.0.113.10",
            userAgent: "private-user-agent");
        var handler = new StationMergedIntegrationEventHandler(activityLogs, logger);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await activityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(log =>
                log.UserId == integrationEvent.ActorUserId
                && log.Action == ActivityLogAction.STATION_MERGED
                && log.SourceEventId == integrationEvent.EventId
                && log.IpAddress == integrationEvent.IpAddress
                && log.UserAgent == integrationEvent.UserAgent
                && log.Metadata == null),
            Arg.Any<CancellationToken>());
        LoggerArguments(logger).Should().NotContain(argument =>
            argument.Contains("203.0.113.10", StringComparison.Ordinal)
            || argument.Contains("private-user-agent", StringComparison.Ordinal)
            || argument.Contains("station-contact@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Normalize_MapsActorActionSourceAndAuditColumns()
    {
        var activityLogs = CreateActivityLogRepository();
        var logger = Substitute.For<ILogger<StationNormalizedIntegrationEventHandler>>();
        var integrationEvent = CreateNormalizedEvent();
        var handler = new StationNormalizedIntegrationEventHandler(activityLogs, logger);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await activityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(log =>
                log.UserId == integrationEvent.ActorUserId
                && log.Action == ActivityLogAction.STATION_NORMALIZED
                && log.SourceEventId == integrationEvent.EventId
                && log.IpAddress == integrationEvent.IpAddress
                && log.UserAgent == integrationEvent.UserAgent
                && log.Metadata == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replay_IsNoOpAndDoesNotAddSecondLog()
    {
        var activityLogs = Substitute.For<IActivityLogRepository>();
        activityLogs.ExistsBySourceEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var integrationEvent = CreateNormalizedEvent();
        var handler = new StationNormalizedIntegrationEventHandler(
            activityLogs,
            Substitute.For<ILogger<StationNormalizedIntegrationEventHandler>>());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task MissingActor_ThrowsBeforeTransactionAndDoesNotCreateMarker()
    {
        var activityLogs = CreateActivityLogRepository();
        var invalidEvent = CreateMergedEvent(actorUserId: Guid.Empty);
        var handler = new StationMergedIntegrationEventHandler(
            activityLogs,
            Substitute.For<ILogger<StationMergedIntegrationEventHandler>>());

        var act = () => handler.HandleAsync(invalidEvent, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task SnapshotIdMismatch_ThrowsBeforeTransactionAndDoesNotCreateMarker()
    {
        var activityLogs = CreateActivityLogRepository();
        var invalidEvent = CreateNormalizedEvent(snapshotStationId: Guid.NewGuid());
        var handler = new StationNormalizedIntegrationEventHandler(
            activityLogs,
            Substitute.For<ILogger<StationNormalizedIntegrationEventHandler>>());

        var act = () => handler.HandleAsync(invalidEvent, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private static IActivityLogRepository CreateActivityLogRepository()
    {
        var repository = Substitute.For<IActivityLogRepository>();
        repository.ExistsBySourceEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        repository.AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ActivityLog>());
        return repository;
    }

    private static StationMergedIntegrationEvent CreateMergedEvent(
        Guid? actorUserId = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var primaryStationId = Guid.NewGuid();
        var duplicateStationId = Guid.NewGuid();
        return new StationMergedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            ActorUserId = actorUserId ?? Guid.NewGuid(),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            PrimaryStationId = primaryStationId,
            DuplicateStationId = duplicateStationId,
            PrimaryBefore = CreateSnapshot(primaryStationId),
            DuplicateBefore = CreateSnapshot(duplicateStationId),
            PrimaryAfter = CreateSnapshot(primaryStationId),
            RelinkedCounts = new StationRelinkedCounts
            {
                OperatorMappings = 1,
                RouteOrigins = 1,
            },
        };
    }

    private static StationNormalizedIntegrationEvent CreateNormalizedEvent(Guid? snapshotStationId = null)
    {
        var stationId = Guid.NewGuid();
        var actualSnapshotId = snapshotStationId ?? stationId;
        return new StationNormalizedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            ActorUserId = Guid.NewGuid(),
            IpAddress = "198.51.100.20",
            UserAgent = "VietRide Admin Web",
            StationId = stationId,
            Before = CreateSnapshot(actualSnapshotId),
            After = CreateSnapshot(actualSnapshotId),
        };
    }

    private static StationAuditSnapshot CreateSnapshot(Guid stationId)
        => new()
        {
            Id = stationId,
            Name = "station-contact@example.com",
            Slug = "station-contact",
            City = "Thu Duc",
            Province = "Ho Chi Minh",
            Latitude = 10.8796m,
            Longitude = 106.8142m,
            SupportsShuttle = true,
            IsActive = true,
        };

    private static IReadOnlyList<string> LoggerArguments<T>(ILogger<T> logger)
        => logger.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .Where(argument => argument is not null)
            .Select(argument => argument!.ToString() ?? string.Empty)
            .ToArray();
}
