using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class BookingHangfireRegistrationTests(
    VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    [Fact]
    public void RegistrationUsesApprovedHangfireServicesAndBookingScheduler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Host=localhost;Port=5432;Database=vietride_booking;Username=vietride;Password=vietride_dev",
                ["Hangfire:SchemaName"] = "hangfire",
                ["Hangfire:QueueName"] = "booking",
                ["Hangfire:WorkerCount"] = "2",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBookingHangfire(configuration);

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IBackgroundJobClient));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IPendingActionRealertScheduler)
            && descriptor.ImplementationType == typeof(HangfirePendingActionRealertScheduler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IScheduleChangeAutoAcceptScheduler)
            && descriptor.ImplementationType == typeof(HangfireScheduleChangeAutoAcceptScheduler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
    }

    [Fact]
    public async Task OrphanResolvedAndLateJobsAreDurableNoOps()
    {
        await factory.InitializeAsync();
        var now = new DateTimeOffset(2026, 7, 15, 4, 0, 0, TimeSpan.Zero);
        var resolved = await SeedActionAsync(now.AddHours(1), resolved: true);
        var late = await SeedActionAsync(now, resolved: false);
        var orphan = Guid.NewGuid();

        await ExecuteInNewScopeAsync(orphan, now);
        await ExecuteInNewScopeAsync(resolved, now);
        await ExecuteInNewScopeAsync(late, now);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var ids = new[] { orphan, resolved, late }.Select(PendingActionRealertJob.DeriveEventId).ToArray();
        foreach (var id in ids)
        {
            var count = await db.Database.SqlQuery<int>(
                    $"SELECT count(*)::int AS \"Value\" FROM vietride_booking.outbox_events WHERE id = {id}")
                .SingleAsync();
            count.Should().Be(0);
        }
    }

    [Fact]
    public async Task ConcurrentPhysicalExecutionsLockAndPersistOneExactDeterministicOutbox()
    {
        await factory.InitializeAsync();
        var now = new DateTimeOffset(2026, 7, 15, 4, 0, 0, TimeSpan.Zero);
        var pendingActionId = await SeedActionAsync(now.AddHours(1), resolved: false);
        factory.SqlCapture.Clear();

        await Task.WhenAll(
            ExecuteInNewScopeAsync(pendingActionId, now),
            ExecuteInNewScopeAsync(pendingActionId, now));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var deterministicId = PendingActionRealertJob.DeriveEventId(pendingActionId);
        var eventType = await db.Database.SqlQuery<string>(
                $"SELECT event_type AS \"Value\" FROM vietride_booking.outbox_events WHERE id = {deterministicId}")
            .SingleAsync();
        var payloadJson = await db.Database.SqlQuery<string>(
                $"SELECT payload::text AS \"Value\" FROM vietride_booking.outbox_events WHERE id = {deterministicId}")
            .SingleAsync();
        eventType.Should().Be("booking.booking.pending_action_realerted");
        using var payload = JsonDocument.Parse(payloadJson);
        var root = payload.RootElement;
        root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).Should().BeEquivalentTo(
            ["eventId", "occurredAt", "bookingId", "tripId", "userId", "pendingActionId", "deadline", "reason", "seatNumbers", "seatImpactReason"]);
        root.GetProperty("eventId").GetGuid().Should().Be(deterministicId);
        root.GetProperty("pendingActionId").GetGuid().Should().Be(pendingActionId);
        root.GetProperty("reason").GetString().Should().Be("PENDING_SEAT_ASSIGNMENT");
        root.GetProperty("seatImpactReason").GetString().Should().Be("SEAT_DISABLED");
        root.GetProperty("seatNumbers").EnumerateArray().Select(item => item.GetString()).Should().Equal("A01", "A02");
        factory.SqlCapture.Commands.Should().Contain(command =>
            command.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Guid> SeedActionAsync(DateTimeOffset deadline, bool resolved)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = new DateTimeOffset(2026, 7, 15, 2, 0, 0, TimeSpan.Zero);
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            null, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000));
        booking.Confirm(now);
        var action = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.PENDING_SEAT_ASSIGNMENT,
            deadline,
            metadata: "{\"sourceEventId\":\"11111111-1111-1111-1111-111111111111\",\"seatNumbers\":[\"A01\",\"A02\"],\"reason\":\"SEAT_DISABLED\"}");
        if (resolved)
        {
            action.Resolve(BookingPendingActionResolved.SUPERSEDED, now);
        }

        db.Bookings.Add(booking);
        db.BookingPendingActions.Add(action);
        await db.SaveChangesAsync();
        return action.Id;
    }

    private async Task ExecuteInNewScopeAsync(Guid pendingActionId, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await new PendingActionRealertJob(db, new FixedClock(now))
            .ExecuteAsync(pendingActionId, CancellationToken.None);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
