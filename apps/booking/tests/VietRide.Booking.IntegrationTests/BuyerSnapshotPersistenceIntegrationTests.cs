using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.BuyerSnapshots;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Booking.Infrastructure.Messaging;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class BuyerSnapshotPersistenceIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory;

    public BuyerSnapshotPersistenceIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ListAndDetail_ReturnSamePersistedBuyerWithoutIdentityAndStayTenantScoped()
    {
        await factory.InitializeAsync();
        var owner = Guid.NewGuid();
        var foreignOwner = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var foreignBookingId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-29T01:00:00Z");
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await InsertBookingAsync(db, bookingId, owner, buyerId, createdAt, withSnapshot: true, withSeat: true);
            await InsertBookingAsync(
                db,
                foreignBookingId,
                foreignOwner,
                Guid.NewGuid(),
                createdAt.AddMinutes(1),
                withSnapshot: true,
                withSeat: false);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var identity = new RecordingIdentityUserServiceClient(new Dictionary<Guid, BookingBuyerSnapshotProfile>());
        var list = await new ListOperatorBookingsQueryHandler(repository, identity).Handle(
            new ListOperatorBookingsQuery(owner, null, null, null, null, null),
            CancellationToken.None);
        var detail = await new GetOperatorBookingDetailQueryHandler(repository, identity).Handle(
            new GetOperatorBookingDetailQuery(bookingId, owner),
            CancellationToken.None);

        var listBuyer = list.Items.Should().ContainSingle().Which.Buyer;
        listBuyer.Should().NotBeNull();
        detail.Buyer.Should().BeEquivalentTo(listBuyer);
        var nonNullListBuyer = listBuyer!;
        nonNullListBuyer.UserId.Should().Be(buyerId);
        nonNullListBuyer.DisplayName.Should().Be("Buyer Snapshot");
        detail.Seats.Should().ContainSingle().Which.SeatNumber.Should().Be("A01");
        identity.RequestedBatches.Should().BeEmpty("persisted snapshots must not require read-time Identity calls");
        (await repository.GetOperatorBookingDetailAsync(foreignBookingId, owner)).Should().BeNull();
    }

    [Fact]
    public async Task LegacyFallback_BackfillPersistsOnceAndReplayIsNoOp()
    {
        await factory.InitializeAsync();
        var owner = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var firstBookingId = Guid.NewGuid();
        var secondBookingId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-29T02:00:00Z");
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await InsertBookingAsync(db, firstBookingId, owner, buyerId, createdAt, withSnapshot: false, withSeat: false);
            await InsertBookingAsync(
                db,
                secondBookingId,
                owner,
                buyerId,
                createdAt.AddMinutes(1),
                withSnapshot: false,
                withSeat: false);
        }

        var profile = new BookingBuyerSnapshotProfile(
            buyerId,
            "Backfilled Buyer",
            "0900000000",
            "buyer@example.test",
            "https://example.test/avatar.jpg",
            false);
        var identity = new RecordingIdentityUserServiceClient(
            new Dictionary<Guid, BookingBuyerSnapshotProfile> { [buyerId] = profile });
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var handler = new ListOperatorBookingsQueryHandler(repository, identity);

        var fallback = await handler.Handle(
            new ListOperatorBookingsQuery(owner, null, null, null, null, null),
            CancellationToken.None);
        fallback.Items.Should().HaveCount(2).And.OnlyContain(item => item.Buyer == new OperatorBookingBuyerDto(
            buyerId,
            profile.DisplayName,
            profile.Phone,
            profile.Email,
            profile.AvatarUrl));
        identity.RequestedBatches.Should().ContainSingle().Which.Should().Equal(buyerId);

        var job = new BuyerSnapshotBackfillJob(repository, identity);
        await job.RunAsync(CancellationToken.None);
        var callsAfterBackfill = identity.RequestedBatches.Count;
        await job.RunAsync(CancellationToken.None);
        identity.RequestedBatches.Should().HaveCount(callsAfterBackfill);

        scope.ServiceProvider.GetRequiredService<BookingDbContext>().ChangeTracker.Clear();
        var persisted = await repository.QueryNoTracking()
            .Where(booking => booking.PassengerUserId == buyerId)
            .OrderBy(booking => booking.Id)
            .ToArrayAsync();
        persisted.Should().HaveCount(2).And.OnlyContain(booking =>
            booking.BuyerDisplayName == profile.DisplayName
            && booking.BuyerPhone == profile.Phone
            && booking.BuyerEmail == profile.Email
            && booking.BuyerAvatarUrl == profile.AvatarUrl);

        var readsBefore = identity.RequestedBatches.Count;
        var snapshottedRead = await handler.Handle(
            new ListOperatorBookingsQuery(owner, null, null, null, null, null),
            CancellationToken.None);
        snapshottedRead.Items.Should().HaveCount(2).And.OnlyContain(item => item.Buyer != null);
        identity.RequestedBatches.Should().HaveCount(readsBefore);
    }

    [Fact]
    public async Task UserDeletedEvent_RedactsExistingAndLegacyRowsIdempotently()
    {
        await factory.InitializeAsync();
        var owner = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var snapshotBookingId = Guid.NewGuid();
        var legacyBookingId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-29T03:00:00Z");
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await InsertBookingAsync(
                db,
                snapshotBookingId,
                owner,
                buyerId,
                createdAt,
                withSnapshot: true,
                withSeat: false);
            await InsertBookingAsync(
                db,
                legacyBookingId,
                owner,
                buyerId,
                createdAt.AddMinutes(1),
                withSnapshot: false,
                withSeat: false);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var consumer = new IdentityUserDeletedIntegrationEventHandler(repository);
        var integrationEvent = new IdentityUserDeletedIntegrationEvent { UserId = buyerId };

        await consumer.HandleAsync(integrationEvent, CancellationToken.None);
        await consumer.HandleAsync(integrationEvent, CancellationToken.None);

        scope.ServiceProvider.GetRequiredService<BookingDbContext>().ChangeTracker.Clear();
        var rows = await repository.QueryNoTracking()
            .Where(booking => booking.PassengerUserId == buyerId)
            .ToArrayAsync();
        rows.Should().HaveCount(2).And.OnlyContain(booking =>
            booking.BuyerDisplayName == BookingBuyerSnapshotProfile.DeletedDisplayName
            && booking.BuyerPhone == null
            && booking.BuyerEmail == null
            && booking.BuyerAvatarUrl == null);
        var result = await new ListOperatorBookingsQueryHandler(
            repository,
            new RecordingIdentityUserServiceClient(new Dictionary<Guid, BookingBuyerSnapshotProfile>()))
            .Handle(new ListOperatorBookingsQuery(owner, null, null, null, null, null), CancellationToken.None);
        result.Items.Should().HaveCount(2).And.OnlyContain(item =>
            item.Buyer != null
            && item.Buyer.DisplayName == BookingBuyerSnapshotProfile.DeletedDisplayName
            && item.Buyer.Phone == null
            && item.Buyer.Email == null
            && item.Buyer.AvatarUrl == null);
    }

    [Fact]
    public async Task RedactionCommittedWhileBackfillWaits_CannotBeOverwrittenByActivePii()
    {
        await factory.InitializeAsync();
        var owner = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-29T04:00:00Z");
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            await InsertBookingAsync(
                seedScope.ServiceProvider.GetRequiredService<BookingDbContext>(),
                bookingId,
                owner,
                buyerId,
                createdAt,
                withSnapshot: false,
                withSeat: false);
        }

        await using var redactionScope = factory.Services.CreateAsyncScope();
        await using var backfillScope = factory.Services.CreateAsyncScope();
        var redactionDb = redactionScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var redactionRepository = redactionScope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var backfillRepository = backfillScope.ServiceProvider.GetRequiredService<IBookingRepository>();
        await using var redactionTransaction = await redactionDb.Database.BeginTransactionAsync();
        (await redactionRepository.RedactBuyerSnapshotsAsync(buyerId)).Should().Be(1);

        var activeProfile = new BookingBuyerSnapshotProfile(
            buyerId,
            "Must Not Reappear",
            "0900000000",
            "leaked@example.test",
            "https://example.test/leaked.jpg",
            false);
        var backfillTask = backfillRepository.ApplyBuyerSnapshotBackfillAsync(
            [new BookingBuyerSnapshotUpdate(bookingId, activeProfile)]);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        backfillTask.IsCompleted.Should().BeFalse("the redaction transaction still owns the row lock");

        await redactionTransaction.CommitAsync();
        (await backfillTask).Should().Be(0, "the atomic null-snapshot predicate must be rechecked after waiting");

        backfillScope.ServiceProvider.GetRequiredService<BookingDbContext>().ChangeTracker.Clear();
        var row = await backfillRepository.QueryNoTracking().SingleAsync(booking => booking.Id == bookingId);
        row.BuyerDisplayName.Should().Be(BookingBuyerSnapshotProfile.DeletedDisplayName);
        row.BuyerPhone.Should().BeNull();
        row.BuyerEmail.Should().BeNull();
        row.BuyerAvatarUrl.Should().BeNull();
    }

    private static async Task InsertBookingAsync(
        BookingDbContext db,
        Guid bookingId,
        Guid operatorId,
        Guid buyerId,
        DateTimeOffset createdAt,
        bool withSnapshot,
        bool withSeat)
    {
        var tripId = Guid.NewGuid();
        var displayName = withSnapshot ? "Buyer Snapshot" : null;
        var phone = withSnapshot ? "0900000000" : null;
        var email = withSnapshot ? "buyer@example.test" : null;
        var avatarUrl = withSnapshot ? "https://example.test/avatar.jpg" : null;
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.bookings (
    id, booking_code, passenger_user_id, buyer_display_name, buyer_phone, buyer_email, buyer_avatar_url,
    trip_id, operator_id, pickup_station_id, base_fare, discount_amount, total_amount, status,
    refund_override, trip_snapshot_origin_name, trip_snapshot_dest_name, trip_snapshot_departure,
    trip_current_departure, trip_snapshot_route_name, created_at, updated_at)
VALUES (
    {bookingId}, {$"VR-{bookingId:N}".Substring(0, 30)}, {buyerId}, {displayName}, {phone}, {email}, {avatarUrl},
    {tripId}, {operatorId}, {Guid.NewGuid()}, 100000, 0, 100000, 'CONFIRMED'::booking_status,
    FALSE, 'Origin', 'Destination', {createdAt.AddDays(1)}, {createdAt.AddDays(1)}, 'Route', {createdAt}, {createdAt});");

        if (!withSeat)
        {
            return;
        }

        var passengerId = Guid.NewGuid();
        var ticketCode = $"VT-20260729-{bookingId:N}"[..20].ToUpperInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.passengers (
    id, booking_id, seat_number, boarding_status, created_at, updated_at)
VALUES ({passengerId}, {bookingId}, 'A01', 'PENDING'::passenger_boarding_status, {createdAt}, {createdAt});");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.tickets (
    id, booking_id, passenger_id, ticket_code, seat_number, status,
    fare_amount, discount_amount, paid_amount, created_at, updated_at)
VALUES ({Guid.NewGuid()}, {bookingId}, {passengerId}, {ticketCode}, 'A01',
    'ISSUED'::ticket_status, 100000, 0, 100000, {createdAt}, {createdAt});");
    }

    private sealed class RecordingIdentityUserServiceClient(
        IReadOnlyDictionary<Guid, BookingBuyerSnapshotProfile> profiles)
        : IIdentityUserServiceClient
    {
        public List<Guid[]> RequestedBatches { get; } = [];

        public Task<Guid?> GetUserIdByPhoneAsync(
            string phone,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Buyer snapshot tests do not use passenger-phone lookup.");

        public Task<IReadOnlyDictionary<Guid, BookingBuyerSnapshotProfile>> GetUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            RequestedBatches.Add(userIds.ToArray());
            return Task.FromResult<IReadOnlyDictionary<Guid, BookingBuyerSnapshotProfile>>(
                profiles
                    .Where(pair => userIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value));
        }
    }
}
