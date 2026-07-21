using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.OutboxDlq;

namespace VietRide.Identity.UnitTests.Application.AdminOutboxDlq;

public sealed class GetAdminOutboxDlqQueryHandlerTests
{
    [Fact]
    public async Task Handle_MergesAvailableSourcesAndReportsUnavailableServices()
    {
        var local = Substitute.For<IAdminOutboxDlqRepository>();
        var sources = Substitute.For<IAdminOutboxDlqSourceClient>();
        var terminalAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var identityItem = CreateItem("identity", terminalAt.AddMinutes(-1));
        var tripItem = CreateItem("trip", terminalAt);

        local.ReadAsync(null, 11, null, null, true, Arg.Any<CancellationToken>())
            .Returns([identityItem]);
        sources.ReadAsync(
                Arg.Any<string>(),
                Arg.Is<string?>(value => value == null),
                Arg.Is(11),
                Arg.Is<DateTimeOffset?>(value => value == null),
                Arg.Is<Guid?>(value => value == null),
                Arg.Is(true),
                Arg.Any<CancellationToken>())
            .Returns(call => string.Equals(call.ArgAt<string>(0), "trip", StringComparison.Ordinal)
                ? Task.FromResult<IReadOnlyList<AdminOutboxDlqItemDto>>([tripItem])
                : Task.FromException<IReadOnlyList<AdminOutboxDlqItemDto>>(new HttpRequestException("source unavailable")));

        var handler = CreateHandler(local, sources);
        var result = await handler.Handle(
            new GetAdminOutboxDlqQuery(null, 10, null, null, "desc"),
            CancellationToken.None);

        result.Items.Select(item => item.Service).Should().Equal("trip", "identity");
        result.UnavailableServices.Should().BeEquivalentTo(["booking", "payment", "parcel", "tracking"]);
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithCursor_ForwardsSourceCursorAndReturnsNextPage()
    {
        var local = Substitute.For<IAdminOutboxDlqRepository>();
        var sources = Substitute.For<IAdminOutboxDlqSourceClient>();
        var newest = CreateItem("identity", DateTimeOffset.Parse("2026-07-18T10:00:00Z"));
        var older = CreateItem("identity", newest.TerminalAt.AddMinutes(-1));

        local.ReadAsync(null, 2, null, null, true, Arg.Any<CancellationToken>())
            .Returns([newest, older]);
        sources.ReadAsync(
                Arg.Any<string>(),
                Arg.Is<string?>(value => value == null),
                Arg.Is(2),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Is(true),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AdminOutboxDlqItemDto>());

        var handler = CreateHandler(local, sources);
        var firstPage = await handler.Handle(
            new GetAdminOutboxDlqQuery(null, 1, "identity", null, "desc"),
            CancellationToken.None);

        firstPage.Items.Should().ContainSingle(item => item.EventId == newest.EventId);
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();

        local.ReadAsync(
                null,
                2,
                newest.TerminalAt,
                newest.EventId,
                true,
                Arg.Any<CancellationToken>())
            .Returns([older]);

        var secondPage = await handler.Handle(
            new GetAdminOutboxDlqQuery(firstPage.NextCursor, 1, "identity", null, "desc"),
            CancellationToken.None);

        secondPage.Items.Should().ContainSingle(item => item.EventId == older.EventId);
        await local.Received(1).ReadAsync(
            null,
            2,
            newest.TerminalAt,
            newest.EventId,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PageSize100_ProbesPastInternalLimitAndReturnsNextCursor()
    {
        var local = Substitute.For<IAdminOutboxDlqRepository>();
        var sources = Substitute.For<IAdminOutboxDlqSourceClient>();
        var terminalAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var firstBatch = Enumerable.Range(1, 100)
            .Select(index => CreateItem(
                "identity",
                terminalAt.AddSeconds(-index),
                Guid.Parse($"41430000-0000-4000-8000-{index:D12}")))
            .ToArray();
        var lookahead = CreateItem(
            "identity",
            terminalAt.AddSeconds(-101),
            Guid.Parse("41430000-0000-4000-8000-000000000101"));

        local.ReadAsync(null, 100, null, null, true, Arg.Any<CancellationToken>())
            .Returns(firstBatch);
        local.ReadAsync(
                null,
                1,
                firstBatch[^1].TerminalAt,
                firstBatch[^1].EventId,
                true,
                Arg.Any<CancellationToken>())
            .Returns([lookahead]);

        var result = await CreateHandler(local, sources).Handle(
            new GetAdminOutboxDlqQuery(null, 100, "identity", null, "desc"),
            CancellationToken.None);

        result.Items.Should().HaveCount(100);
        result.NextCursor.Should().NotBeNullOrWhiteSpace();
        await local.Received(1).ReadAsync(
            null,
            1,
            firstBatch[^1].TerminalAt,
            firstBatch[^1].EventId,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DescendingCompositeCursor_ExcludesHigherServiceAtSameTimestamp()
    {
        var local = Substitute.For<IAdminOutboxDlqRepository>();
        var sources = Substitute.For<IAdminOutboxDlqSourceClient>();
        var terminalAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var eventId = Guid.Parse("41430000-0000-4000-8000-000000000050");
        var cursor = EncodeCursor("payment", terminalAt, eventId);

        local.ReadAsync(
                null,
                11,
                terminalAt,
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                true,
                Arg.Any<CancellationToken>())
            .Returns([]);
        sources.ReadAsync(
                Arg.Any<string>(),
                Arg.Is<string?>(value => value == null),
                Arg.Is(11),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Is(true),
                Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler(local, sources).Handle(
            new GetAdminOutboxDlqQuery(cursor, 10, null, null, "desc"),
            CancellationToken.None);

        await sources.Received(1).ReadAsync(
            "tracking",
            null,
            11,
            terminalAt,
            Guid.Empty,
            true,
            Arg.Any<CancellationToken>());
        await sources.Received(1).ReadAsync(
            "booking",
            null,
            11,
            terminalAt,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            true,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("desc", "trip", "tracking", "payment", "parcel", "identity", "booking")]
    [InlineData("asc", "booking", "identity", "parcel", "payment", "tracking", "trip")]
    public async Task Handle_CompositeCursor_PaginatesAllServicesWithSamePostgresTimestamp(
        string sortDir,
        params string[] expectedServices)
    {
        var local = Substitute.For<IAdminOutboxDlqRepository>();
        var sources = Substitute.For<IAdminOutboxDlqSourceClient>();
        var terminalAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var items = new[]
        {
            CreateItem("identity", terminalAt, Guid.Parse("41430000-0000-4000-8000-000000000001")),
            CreateItem("trip", terminalAt, Guid.Parse("41430000-0000-4000-8000-000000000002")),
            CreateItem("booking", terminalAt, Guid.Parse("41430000-0000-4000-8000-000000000003")),
            CreateItem("payment", terminalAt, Guid.Parse("41430000-0000-4000-8000-000000000004")),
            CreateItem("parcel", terminalAt, Guid.Parse("41430000-0000-4000-8000-000000000005")),
            CreateItem("tracking", terminalAt, Guid.Parse("41430000-0000-4000-8000-000000000006")),
        };

        local.ReadAsync(
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(call => ReadLikePostgres(
                items.Where(item => item.Service == "identity"),
                call.ArgAt<int>(1),
                call.ArgAt<DateTimeOffset?>(2),
                call.ArgAt<Guid?>(3),
                call.ArgAt<bool>(4)));
        sources.ReadAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(call => ReadLikePostgres(
                items.Where(item => item.Service == call.ArgAt<string>(0)),
                call.ArgAt<int>(2),
                call.ArgAt<DateTimeOffset?>(3),
                call.ArgAt<Guid?>(4),
                call.ArgAt<bool>(5)));

        var handler = CreateHandler(local, sources);
        var actualServices = new List<string>();
        string? cursor = null;

        for (var pageNumber = 0; pageNumber < 3; pageNumber++)
        {
            var page = await handler.Handle(
                new GetAdminOutboxDlqQuery(cursor, 2, null, null, sortDir),
                CancellationToken.None);

            page.Items.Should().HaveCount(2);
            actualServices.AddRange(page.Items.Select(item => item.Service));
            cursor = page.NextCursor;
            if (pageNumber < 2)
                cursor.Should().NotBeNullOrWhiteSpace();
        }

        cursor.Should().BeNull();
        actualServices.Should().Equal(expectedServices);
    }

    [Theory]
    [InlineData(0, null, null, "desc")]
    [InlineData(101, null, null, "desc")]
    [InlineData(50, "unknown", null, "desc")]
    [InlineData(50, null, "not-base64", "desc")]
    [InlineData(50, null, null, "sideways")]
    public void Validator_RejectsInvalidPagingServiceCursorOrSort(
        int pageSize,
        string? service,
        string? cursor,
        string sortDir)
    {
        var validator = new GetAdminOutboxDlqQueryValidator();

        var result = validator.Validate(new GetAdminOutboxDlqQuery(cursor, pageSize, service, null, sortDir));

        result.IsValid.Should().BeFalse();
    }

    private static GetAdminOutboxDlqQueryHandler CreateHandler(
        IAdminOutboxDlqRepository local,
        IAdminOutboxDlqSourceClient sources)
        => new(local, sources, NullLogger<GetAdminOutboxDlqQueryHandler>.Instance);

    private static string EncodeCursor(string service, DateTimeOffset terminalAt, Guid eventId)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Service = service,
            TerminalAt = terminalAt,
            EventId = eventId,
        })))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static AdminOutboxDlqItemDto CreateItem(
        string service,
        DateTimeOffset terminalAt,
        Guid? eventId = null)
    {
        using var document = JsonDocument.Parse("{\"bookingId\":\"redacted\"}");
        return new AdminOutboxDlqItemDto(
            service,
            eventId ?? Guid.NewGuid(),
            "booking.booking.confirmed",
            document.RootElement.Clone(),
            6,
            "publisher unavailable",
            terminalAt.AddHours(-1),
            terminalAt);
    }

    private static IReadOnlyList<AdminOutboxDlqItemDto> ReadLikePostgres(
        IEnumerable<AdminOutboxDlqItemDto> source,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterEventId,
        bool descending)
    {
        DateTimeOffset? normalizedTerminalAt = afterTerminalAt.HasValue
            ? new DateTimeOffset(
                afterTerminalAt.Value.Ticks - (afterTerminalAt.Value.Ticks % 10),
                afterTerminalAt.Value.Offset)
            : null;
        var filtered = source;

        if (normalizedTerminalAt.HasValue && afterEventId.HasValue)
        {
            filtered = descending
                ? filtered.Where(item => item.TerminalAt < normalizedTerminalAt.Value
                    || (item.TerminalAt == normalizedTerminalAt.Value && item.EventId.CompareTo(afterEventId.Value) < 0))
                : filtered.Where(item => item.TerminalAt > normalizedTerminalAt.Value
                    || (item.TerminalAt == normalizedTerminalAt.Value && item.EventId.CompareTo(afterEventId.Value) > 0));
        }

        return (descending
                ? filtered.OrderByDescending(item => item.TerminalAt).ThenByDescending(item => item.EventId)
                : filtered.OrderBy(item => item.TerminalAt).ThenBy(item => item.EventId))
            .Take(pageSize)
            .ToArray();
    }
}
