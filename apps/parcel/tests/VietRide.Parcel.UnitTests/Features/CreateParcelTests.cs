using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class CreateParcelTests
{
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid DropoffStopId = Guid.NewGuid();
    private static readonly Guid BookingId = Guid.NewGuid();
    private static readonly Guid RecipientUserId = Guid.NewGuid();
    private const string RecipientName = "Nguyen Van A";
    private const string RecipientPhone = "+84912345678";
    private const string RecipientEmail = "a@example.com";
    private const string ItemName = "Box of goods";
    private const string Description = "Fragile";
    private const string PhotoUrl = "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg";
    private const decimal WeightKg = 3.5m;
    private static readonly DateTimeOffset Departure = new(2026, 7, 15, 8, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset EstimatedArrival = new(2026, 7, 15, 18, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset Now = new(2026, 6, 29, 10, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public async Task Create_PersistsStableTripDisplaySnapshot()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);

        await handler.Handle(BuildCommand(), CancellationToken.None);

        await parcelRepo.Received(1).AddAsync(
            Arg.Is<ParcelEntity>(parcel =>
                parcel.TripSnapshotRouteId == RouteId
                && parcel.TripSnapshotRouteName == "HCM - Da Lat"
                && parcel.TripSnapshotOriginStationName == "Mien Dong"
                && parcel.TripSnapshotDestinationStationName == "Da Lat"
                && parcel.TripSnapshotVehicleId == VehicleId
                && parcel.TripSnapshotVehicleLicensePlate == "51B-12345"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ExistingRecipientEmail_PersistsResolvedRecipientUserId()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);
        ParcelEntity? captured = null;
        parcelRepo.AddAsync(Arg.Any<ParcelEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ParcelEntity>();
                return Task.FromResult(captured);
            });

        await CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow)
            .Handle(BuildCommand(), CancellationToken.None);

        captured!.RecipientUserId.Should().Be(RecipientUserId);
        await identity.Received(1).FindUserByEmailAsync(
            RecipientEmail,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_RecipientEmailWithOuterWhitespaceAndUppercase_PersistsNormalizedEmail()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);
        ParcelEntity? captured = null;
        parcelRepo.AddAsync(Arg.Any<ParcelEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ParcelEntity>();
                return Task.FromResult(captured);
            });

        await CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow)
            .Handle(
                BuildCommand(recipientEmail: "  A@EXAMPLE.COM  "),
                CancellationToken.None);

        captured!.RecipientEmail.Should().Be(RecipientEmail);
        captured.RecipientUserId.Should().Be(RecipientUserId);
        await identity.Received(1).FindUserByEmailAsync(
            RecipientEmail,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_UnknownRecipientEmail_PersistsNullRecipientUserId()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.NotFound());
        ParcelEntity? captured = null;
        parcelRepo.AddAsync(Arg.Any<ParcelEntity>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ParcelEntity>();
                return Task.FromResult(captured);
            });

        await CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow)
            .Handle(BuildCommand(), CancellationToken.None);

        captured!.RecipientUserId.Should().BeNull();
    }

    [Fact]
    public async Task Create_RecipientLookupUnavailable_FailsBeforePersistence()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.TransportFailure("identity unavailable"));

        var act = () => CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow)
            .Handle(BuildCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        await parcelRepo.DidNotReceive().AddAsync(Arg.Any<ParcelEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenTripDisplaySummaryUnavailable_FailsBeforePersistence()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.TransportFailure("trip summary unavailable"));
        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);

        var exception = await Assert.ThrowsAsync<ParcelDependencyUnavailableException>(() =>
            handler.Handle(BuildCommand(), CancellationToken.None));

        exception.ErrorCode.Should().Be("TRIP_SERVICE_UNAVAILABLE");
        await parcelRepo.DidNotReceive().AddAsync(
            Arg.Any<ParcelEntity>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenParcelEntitlementIsBlocked_PreservesCanonicalErrorBeforeSideEffects()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);
        identity.GetSubscriptionWriteEligibilityAsync(
                OperatorId,
                requireParcelModule: true,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionWriteEligibilityOutcome.Rejected(
                403,
                "SUBSCRIPTION_MODULE_DISABLED",
                "Parcel module is disabled for the operator subscription."));
        var payment = CreatePaymentClient();
        var outbox = Outbox();
        var stats = Stats();
        var handler = CreateHandler(
            identity,
            booking,
            trip,
            parcelRepo,
            fareRepo,
            uow,
            payment,
            outbox,
            stats);

        var exception = await Assert.ThrowsAsync<SubscriptionWriteBlockedException>(() =>
            handler.Handle(BuildCommand(), CancellationToken.None));

        exception.StatusCode.Should().Be(403);
        exception.ErrorCode.Should().Be("SUBSCRIPTION_MODULE_DISABLED");
        await identity.Received(1).GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true,
            Arg.Any<CancellationToken>());
        await parcelRepo.DidNotReceive().AddAsync(
            Arg.Any<ParcelEntity>(),
            Arg.Any<CancellationToken>());
        await payment.DidNotReceive().ChargeParcelPaymentAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<PaymentContextSnapshot?>());
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await stats.DidNotReceive().UpsertIncrementAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateOnly>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ParcelOnly_NormalSize_ReturnsPendingPayment()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand(sizeCategory: "MEDIUM", deliveryMethod: "TERMINAL_PICKUP");

        var result = await handler.Handle(command, CancellationToken.None);

        result.Status.Should().Be("PENDING_PAYMENT");
        result.PaymentRedirectUrl.Should().BeNull();
        result.ParcelId.Should().NotBeEmpty();
        result.ParcelCode.Should().NotBeNullOrEmpty();
        result.DepositRequiredVnd.Should().Be(30_000);
        result.EstimatedTotalPriceVnd.Should().Be(150_000);
    }

    [Fact]
    public async Task Create_ExtraLarge_ReturnsPendingPayment_WithoutReviewRequest()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);

        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow, outbox: outbox);
        var command = BuildCommand(
            sizeCategory: "EXTRA_LARGE",
            deliveryMethod: "TERMINAL_PICKUP",
            estimatedWeightKg: 31m);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Status.Should().Be("PENDING_PAYMENT");
        await parcelRepo.Received(1).AddAsync(
            Arg.Is<ParcelEntity>(parcel => parcel.ReviewDecision == null),
            Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(
            ParcelOutboxEvents.ReviewRequested,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("MEDIUM")]
    [InlineData("EXTRA_LARGE")]
    public async Task Create_PersistsTrimmedPhotoUrl_ForEveryCreatePath(string sizeCategory)
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand(sizeCategory: sizeCategory, photoUrl: $"  {PhotoUrl}  ");

        await handler.Handle(command, CancellationToken.None);

        await parcelRepo.Received(1).AddAsync(
            Arg.Is<ParcelEntity>(parcel => parcel.PhotoUrl == PhotoUrl),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Create_NormalizesMissingPhotoUrlToNull(string? photoUrl)
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);

        await handler.Handle(BuildCommand(photoUrl: photoUrl), CancellationToken.None);

        await parcelRepo.Received(1).AddAsync(
            Arg.Is<ParcelEntity>(parcel => parcel.PhotoUrl == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Rejects_DoorDelivery()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: true);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand(sizeCategory: "MEDIUM", deliveryMethod: "DOOR_DELIVERY");

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("INVALID_DELIVERY_METHOD");
    }

    [Fact]
    public async Task Create_Returns_BookingServiceUnavailable_WhenBookingEndpointFails()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, "PASSENGER", null, "ACTIVE"), null));
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.Success(RecipientUserId));

        booking.GetBookingSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(new BookingLookupOutcome(BookingLookupOutcomeKind.TransportError, null, "booking down"));

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand(bookingId: BookingId);

        var ex = await Assert.ThrowsAsync<ParcelDependencyUnavailableException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("BOOKING_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Create_Returns_BookingNotFound_WhenBookingIdInvalid()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, "PASSENGER", null, "ACTIVE"), null));
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.Success(RecipientUserId));

        booking.GetBookingSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(new BookingLookupOutcome(BookingLookupOutcomeKind.BookingNotFound, null, null));

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand(bookingId: BookingId);

        var ex = await Assert.ThrowsAsync<CodedNotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("BOOKING_NOT_FOUND");
    }

    [Fact]
    public async Task Create_Returns_BookNotOwnedBySender_WhenUserIdMismatch()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        var otherUserId = Guid.NewGuid();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, "PASSENGER", null, "ACTIVE"), null));
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.Success(RecipientUserId));

        booking.GetBookingSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(new BookingLookupOutcome(BookingLookupOutcomeKind.Success,
                new BookingSnapshot(BookingId, otherUserId, TripId, "CONFIRMED"), null));

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand(bookingId: BookingId);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("BOOKING_NOT_OWNED_BY_SENDER");
    }

    [Fact]
    public async Task Create_Returns_TripNotFound_WhenTripMissing()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: null,
            hasBooking: false,
            hasFare: true);

        trip.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.TripNotFound, null, null));

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand();

        var ex = await Assert.ThrowsAsync<CodedNotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("TRIP_NOT_FOUND");
    }

    [Theory]
    [InlineData("BOARDING")]
    [InlineData("IN_PROGRESS")]
    public async Task Create_Returns_TripNotAcceptingParcel_WhenNotScheduled(string tripStatus)
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: tripStatus,
            hasBooking: false,
            hasFare: true);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand();

        var ex = await Assert.ThrowsAsync<CodedConflictException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("TRIP_NOT_ACCEPTING_PARCEL");
        await parcelRepo.DidNotReceive().AddAsync(Arg.Any<ParcelEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Returns_FareNotConfigured_WhenNoFareForRoute()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: false);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand();

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("FARE_NOT_CONFIGURED");
    }

    [Fact]
    public async Task Create_ExtraLargeWithoutFare_ReturnsFareNotConfigured_WithoutWrites()
    {
        var (identity, booking, trip, parcelRepo, fareRepo, uow) = SetupMocks(
            userRole: "PASSENGER",
            userStatus: "ACTIVE",
            tripStatus: "SCHEDULED",
            hasBooking: false,
            hasFare: false);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var stats = Substitute.For<IParcelStatsRepository>();
        var handler = CreateHandler(
            identity, booking, trip, parcelRepo, fareRepo, uow,
            outbox: outbox,
            stats: stats);

        var act = () => handler.Handle(
            BuildCommand(sizeCategory: "EXTRA_LARGE"),
            CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "FARE_NOT_CONFIGURED");
        await parcelRepo.DidNotReceive().AddAsync(
            Arg.Any<ParcelEntity>(),
            Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await stats.DidNotReceive().UpsertIncrementAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateOnly>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Returns_UserNotFound_WhenIdentityUserMissing()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.UserNotFound, null, null));

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand();

        var ex = await Assert.ThrowsAsync<CodedNotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Create_Returns_UserNotPassenger_WhenWrongRole()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, "DRIVER", null, "ACTIVE"), null));

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("USER_NOT_PASSENGER");
    }

    [Fact]
    public async Task Create_Returns_UserInactive_WhenStatusInactive()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, "PASSENGER", null, "INACTIVE"), null));

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("USER_INACTIVE");
    }

    [Fact]
    public async Task Create_ParcelCodeCollision_RetriesUpTo3Times()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, "PASSENGER", null, "ACTIVE"), null));
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.Success(RecipientUserId));

        trip.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot("SCHEDULED"), null));
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success(
            [
                new TripSummarySnapshot(
                    TripId,
                    "SCHEDULED",
                    Departure,
                    EstimatedArrival,
                    new TripRouteSummarySnapshot(RouteId, "HCM - Da Lat", "Mien Dong", "Da Lat"),
                    new TripVehicleSummarySnapshot(VehicleId, "51B-12345", "ACTIVE")),
            ]));

        var fare = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.SMALL, OperatorId,
            Money.FromRaw(150_000), Now);
        fare.UpdateWeightPricing(Money.FromRaw(1), Money.FromRaw(150_000));
        fareRepo.FindByCompositeAsync(RouteId, ParcelSizeCategory.SMALL, Arg.Any<CancellationToken>())
            .Returns(fare);

        var existingParcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260629-XXXXXXXX", SenderUserId, RecipientUserId, RecipientName,
            PhoneNumber.Normalize(RecipientPhone), RecipientEmail, OperatorId, TripId,
            DropoffStopId, null, Description, PhotoUrl, ParcelSizeCategory.MEDIUM,
            WeightKg, ParcelDeliveryMethod.TERMINAL_PICKUP, fare.PriceVnd);

        parcelRepo.FindByParcelCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existingParcel);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand();

        var ex = await Assert.ThrowsAsync<CodedConflictException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("PARCEL_CODE_COLLISION");

        await parcelRepo.Received(3).FindByParcelCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithBooking_Success()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, "PASSENGER", null, "ACTIVE"), null));
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.Success(RecipientUserId));

        booking.GetBookingSnapshotAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(new BookingLookupOutcome(BookingLookupOutcomeKind.Success,
                new BookingSnapshot(BookingId, SenderUserId, TripId, "CONFIRMED", ActiveTicketCount: 1), null));

        trip.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot("SCHEDULED"), null));
        trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(TripSummaryBatchOutcome.Success(
            [
                new TripSummarySnapshot(
                    TripId,
                    "SCHEDULED",
                    Departure,
                    EstimatedArrival,
                    new TripRouteSummarySnapshot(RouteId, "HCM - Da Lat", "Mien Dong", "Da Lat"),
                    new TripVehicleSummarySnapshot(VehicleId, "51B-12345", "ACTIVE")),
            ]));

        var fare = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.SMALL, OperatorId,
            Money.FromRaw(150_000), Now);
        fare.UpdateWeightPricing(Money.FromRaw(1), Money.FromRaw(150_000));
        fareRepo.FindByCompositeAsync(RouteId, ParcelSizeCategory.SMALL, Arg.Any<CancellationToken>())
            .Returns(fare);

        parcelRepo.FindByParcelCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ParcelEntity?)null);

        ParcelEntity? captured = null;
        parcelRepo.AddAsync(Arg.Any<ParcelEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<ParcelEntity>(0);
                return Task.FromResult(captured);
            });

        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var handler = CreateHandler(identity, booking, trip, parcelRepo, fareRepo, uow);
        var command = BuildCommand(bookingId: BookingId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Status.Should().Be("PENDING_PAYMENT");
        result.ParcelId.Should().NotBeEmpty();
        result.ParcelCode.Should().NotBeNullOrEmpty();
        result.DepositRequiredVnd.Should().Be(30_000);

        captured.Should().NotBeNull();
        captured!.BookingId.Should().Be(BookingId);
        captured.TripId.Should().Be(TripId);
    }

    private static CreateParcelCommand BuildCommand(
        Guid? bookingId = null,
        string sizeCategory = "MEDIUM",
        string deliveryMethod = "TERMINAL_PICKUP",
        string paymentMethod = "VNPAY",
        string? photoUrl = PhotoUrl,
        decimal estimatedWeightKg = WeightKg,
        string? recipientEmail = RecipientEmail)
    {
        return new CreateParcelCommand(
            SenderUserId,
            RecipientUserId,
            RecipientName,
            RecipientPhone,
            recipientEmail,
            TripId,
            DropoffStopId,
            bookingId,
            ItemName,
            Description,
            photoUrl,
            sizeCategory,
            1m,
            1m,
            1m,
            estimatedWeightKg,
            deliveryMethod,
            paymentMethod,
            null,
            null,
            null);
    }

    private static (IIdentityServiceClient, IBookingServiceClient, ITripServiceClient,
        IParcelRepository, IParcelRouteFareRepository, IUnitOfWork) SetupMocks(
        string userRole = "PASSENGER",
        string userStatus = "ACTIVE",
        string? tripStatus = "SCHEDULED",
        bool hasBooking = false,
        bool hasFare = true)
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        var booking = Substitute.For<IBookingServiceClient>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcelRepo = Substitute.For<IParcelRepository>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();
        var uow = Substitute.For<IUnitOfWork>();

        identity.GetUserInfoAsync(SenderUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupOutcome(UserLookupOutcomeKind.Success,
                new IdentityUserInfo(SenderUserId, userRole, null, userStatus), null));
        identity.FindUserByEmailAsync(RecipientEmail, Arg.Any<CancellationToken>())
            .Returns(RecipientUserLookupOutcome.Success(RecipientUserId));

        if (tripStatus != null)
        {
            trip.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
                .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success,
                    CreateTripSnapshot(tripStatus), null));
            trip.GetTripSummariesAsync(
                    Arg.Any<IReadOnlyCollection<Guid>>(),
                    Arg.Any<CancellationToken>())
                .Returns(TripSummaryBatchOutcome.Success(
                [
                    new TripSummarySnapshot(
                        TripId,
                        tripStatus,
                        Departure,
                        EstimatedArrival,
                        new TripRouteSummarySnapshot(RouteId, "HCM - Da Lat", "Mien Dong", "Da Lat"),
                        new TripVehicleSummarySnapshot(VehicleId, "51B-12345", "ACTIVE")),
                ]));
        }

        if (hasFare)
        {
            foreach (var category in Enum.GetValues<ParcelSizeCategory>())
            {
                var fare = ParcelRouteFare.Create(RouteId, category, OperatorId,
                    Money.FromRaw(150_000), Now);
                fare.UpdateWeightPricing(Money.FromRaw(1), Money.FromRaw(150_000));
                fareRepo.FindByCompositeAsync(RouteId, category, Arg.Any<CancellationToken>())
                    .Returns(fare);
            }
        }

        parcelRepo.FindByParcelCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ParcelEntity?)null);

        ParcelEntity? captured = null;
        parcelRepo.AddAsync(Arg.Any<ParcelEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<ParcelEntity>(0);
                return Task.FromResult(captured);
            });

        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        return (identity, booking, trip, parcelRepo, fareRepo, uow);
    }

    private static CreateParcelCommandHandler CreateHandler(
        IIdentityServiceClient identity,
        IBookingServiceClient booking,
        ITripServiceClient trip,
        IParcelRepository parcelRepo,
        IParcelRouteFareRepository fareRepo,
        IUnitOfWork uow,
        IPaymentServiceClient? payment = null,
        IIntegrationEventOutbox? outbox = null,
        IParcelStatsRepository? stats = null)
    {
        payment ??= CreatePaymentClient();
        outbox ??= Outbox();
        stats ??= Stats();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new CreateParcelCommandHandler(
            identity,
            booking,
            trip,
            payment,
            parcelRepo,
            fareRepo,
            policyRepository: null,
            uow,
            outbox,
            stats,
            NullLogger<CreateParcelCommandHandler>.Instance,
            clock);
    }

    private static IPaymentServiceClient CreatePaymentClient()
    {
        var client = Substitute.For<IPaymentServiceClient>();
        client.ChargeParcelPaymentAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>(), Arg.Any<PaymentContextSnapshot?>())
            .Returns(new ChargeOutcome(ChargeOutcomeKind.Success,
                new ChargeResult(Guid.NewGuid(), "SUCCEEDED", null), null));
        return client;
    }

    private static IIntegrationEventOutbox Outbox()
        => Substitute.For<IIntegrationEventOutbox>();

    private static IParcelStatsRepository Stats()
        => Substitute.For<IParcelStatsRepository>();

    private static TripParcelSnapshot CreateTripSnapshot(string status)
    {
        var station = new TripStationDto(Guid.NewGuid(), "Station");
        return new TripParcelSnapshot(
            TripId, OperatorId, RouteId, VehicleId, status,
            Departure, EstimatedArrival, 100_000,
            station, station,
            new List<TripStopDto>
            {
                new(DropoffStopId, 1, false, true, EstimatedArrival, 10, null, "PENDING", null),
            },
            new TripSeatSummaryDto(40, 35),
            null);
    }
}
