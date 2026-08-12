using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentValidation.TestHelper;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Application.Features.PassengerHistory;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.PassengerHistory;

public sealed class GetPassengerHistoryQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TicketBranch_ForwardsBookingRedirectAndNeverCallsPayment()
    {
        var userId = Guid.NewGuid();
        var bookingClient = Substitute.For<IBookingServiceClient>();
        var parcelRepository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var dropoffStopId = Guid.NewGuid();
        var booking = new BookingHistoryItemDto(
            Guid.NewGuid(),
            "VR-20260701-ABCDEFGH",
            Guid.NewGuid(),
            "CONFIRMED",
            DateTimeOffset.UtcNow,
            350_000,
            "Origin",
            "Destination",
            DateTimeOffset.UtcNow.AddDays(1),
            null,
            null,
            "Route",
            [new BookingHistoryTicketDto(Guid.NewGuid(), "VT-20260701-ABCDEFGH", "A01", "ISSUED", 350_000)],
            "https://sandbox.vnpayment.vn/ticket",
            DropoffStopId: dropoffStopId,
            Vehicle: new BookingHistoryVehicleDto(
                "51B-123.45",
                new BookingHistoryVehicleTypeDto("LIMOUSINE", "Limousine")));
        bookingClient.GetPassengerHistoryAsync(
                userId,
                "CONFIRMED",
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(new BookingHistoryOutcome(
                true,
                new BookingHistoryPage([booking], 1, 20, 1, 1, false, false),
                null));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        var handler = CreateHandler(bookingClient, parcelRepository, tripClient, paymentLookup);

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "TICKET", "CONFIRMED", null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].Type.Should().Be("TICKET");
        result.Items[0].Ticket.Should().NotBeNull();
        result.Items[0].Parcel.Should().BeNull();
        result.Items[0].Ticket!.Tickets.Should().ContainSingle();
        result.Items[0].Ticket!.Vehicle.Should().BeEquivalentTo(new PassengerHistoryVehicleDto(
            "51B-123.45",
            new PassengerHistoryVehicleTypeDto("LIMOUSINE", "Limousine")));
        result.Items[0].PaymentRedirectUrl.Should().Be("https://sandbox.vnpayment.vn/ticket");
        result.Items[0].TrackingTarget.Should().BeEquivalentTo(new
        {
            Kind = "STOP",
            StopId = (Guid?)dropoffStopId,
            StationId = (Guid?)null,
        });
        await paymentLookup.DidNotReceiveWithAnyArgs().LookupAsync(default, default!, default);
        await parcelRepository.DidNotReceive().ListSentByUserIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<ParcelStatus?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParcelBranch_ReturnsPhotoUrlAndCallsOnlySenderScopedLocalRepository()
    {
        const string photoUrl = "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg";
        var userId = Guid.NewGuid();
        var bookingClient = Substitute.For<IBookingServiceClient>();
        var parcelRepository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var destinationStationId = Guid.NewGuid();
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-PCL-HISTORY",
            userId,
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Fragile",
            photoUrl,
            ParcelSizeCategory.SMALL,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(50_000));
        parcelRepository.ListSentByUserIdAsync(
                userId,
                ParcelStatus.IN_TRANSIT,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        tripClient.GetTripParcelSnapshotAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                new TripParcelSnapshot(
                    parcel.TripId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "SCHEDULED",
                    Now.AddDays(1),
                    Now.AddDays(1).AddHours(4),
                    50_000,
                    new TripStationDto(Guid.NewGuid(), "Origin"),
                    new TripStationDto(destinationStationId, "Destination"),
                    [],
                    new TripSeatSummaryDto(40, 40),
                    null),
                null));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        var handler = CreateHandler(bookingClient, parcelRepository, tripClient, paymentLookup);

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", "IN_TRANSIT", null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].Parcel.Should().NotBeNull();
        result.Items[0].Parcel!.PhotoUrl.Should().Be(photoUrl);
        result.Items[0].PaymentRedirectUrl.Should().BeNull();
        result.Items[0].TrackingTarget.Should().BeEquivalentTo(new
        {
            Kind = "STATION",
            StopId = (Guid?)null,
            StationId = (Guid?)destinationStationId,
        });
        await bookingClient.DidNotReceive().GetPassengerHistoryAsync(
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TicketBranch_BookingFailure_Throws502Exception()
    {
        var bookingClient = Substitute.For<IBookingServiceClient>();
        bookingClient.GetPassengerHistoryAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new BookingHistoryOutcome(false, null, "offline"));
        var handler = CreateHandler(
            bookingClient,
            Substitute.For<IParcelRepository>(),
            Substitute.For<ITripServiceClient>(),
            Substitute.For<IPaymentRedirectLookupClient>());

        var action = () => handler.Handle(
            new GetPassengerHistoryQuery(Guid.NewGuid(), "TICKET", null, null, null, 1, 20),
            CancellationToken.None);

        var exception = (await action.Should()
            .ThrowAsync<PassengerHistoryUpstreamUnavailableException>()).Which;
        exception.StatusCode.Should().Be(502);
        exception.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    [Fact]
    public async Task ParcelBranch_EnrichesDepositAndFinalCandidatesWithOneDeduplicatedLookup()
    {
        var userId = Guid.NewGuid();
        var depositPaymentId = Guid.NewGuid();
        var balancePaymentId = Guid.NewGuid();
        var deposit = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_PAYMENT,
            depositPaymentId: depositPaymentId,
            depositRequired: 40_000,
            depositPaid: 10_000,
            latestCheckInAt: Now.AddMinutes(20));
        var final = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_FINAL_PAYMENT,
            balancePaymentId: balancePaymentId,
            balanceRequired: 80_000,
            balancePaid: 30_000,
            finalPaymentDeadline: Now.AddMinutes(15));
        var repository = RepositoryWithPage(userId, deposit, final);
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        paymentLookup.LookupAsync(
                userId,
                Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(),
                Arg.Any<CancellationToken>())
            .Returns([
                LookupItem(depositPaymentId, "PARCEL", deposit.Id, 30_000, Now.AddMinutes(10),
                    "https://sandbox.vnpayment.vn/deposit"),
                LookupItem(balancePaymentId, "PARCEL_ADDITIONAL", final.Id, 50_000, Now.AddMinutes(15),
                    "https://sandbox.vnpayment.vn/final"),
            ]);
        var handler = CreateHandler(
            Substitute.For<IBookingServiceClient>(),
            repository,
            TripClientUnavailable(),
            paymentLookup);

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Single(item => item.Id == deposit.Id).PaymentRedirectUrl
            .Should().Be("https://sandbox.vnpayment.vn/deposit");
        result.Items.Single(item => item.Id == final.Id).PaymentRedirectUrl
            .Should().Be("https://sandbox.vnpayment.vn/final");
        await paymentLookup.Received(1).LookupAsync(
            userId,
            Arg.Is<IReadOnlyCollection<PaymentRedirectLookupReference>>(references =>
                references.Count == 2
                && references.Count(reference => reference.ReferenceType == "PARCEL"
                    && reference.ReferenceId == deposit.Id) == 1
                && references.Count(reference => reference.ReferenceType == "PARCEL_ADDITIONAL"
                    && reference.ReferenceId == final.Id) == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParcelBranch_DeduplicatesRepeatedReferenceBeforeLookup()
    {
        var userId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var parcel = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_PAYMENT,
            depositPaymentId: paymentId,
            depositRequired: 40_000,
            latestCheckInAt: Now.AddMinutes(20));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        paymentLookup.LookupAsync(
                userId,
                Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(),
                Arg.Any<CancellationToken>())
            .Returns([
                LookupItem(paymentId, "PARCEL", parcel.Id, 40_000, Now.AddMinutes(10),
                    "https://sandbox.vnpayment.vn/deposit"),
            ]);
        var handler = CreateHandler(
            Substitute.For<IBookingServiceClient>(),
            RepositoryWithPage(userId, parcel, parcel),
            TripClientUnavailable(),
            paymentLookup);

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().HaveCount(2).And
            .OnlyContain(item => item.PaymentRedirectUrl == "https://sandbox.vnpayment.vn/deposit");
        await paymentLookup.Received(1).LookupAsync(
            userId,
            Arg.Is<IReadOnlyCollection<PaymentRedirectLookupReference>>(references =>
                references.Count == 1
                && references.Single().ReferenceType == "PARCEL"
                && references.Single().ReferenceId == parcel.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParcelBranch_RejectsWrongPaymentAmountAndDeadline()
    {
        var userId = Guid.NewGuid();
        var wrongPaymentId = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_PAYMENT,
            depositPaymentId: Guid.NewGuid(),
            depositRequired: 40_000,
            depositPaid: 10_000,
            latestCheckInAt: Now.AddMinutes(20));
        var wrongAmount = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_PAYMENT,
            depositPaymentId: Guid.NewGuid(),
            depositRequired: 50_000,
            depositPaid: 10_000,
            latestCheckInAt: Now.AddMinutes(20));
        var lateFinal = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_FINAL_PAYMENT,
            balancePaymentId: Guid.NewGuid(),
            balanceRequired: 80_000,
            balancePaid: 30_000,
            finalPaymentDeadline: Now.AddMinutes(15));
        var repository = RepositoryWithPage(userId, wrongPaymentId, wrongAmount, lateFinal);
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        paymentLookup.LookupAsync(
                userId,
                Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(),
                Arg.Any<CancellationToken>())
            .Returns([
                LookupItem(Guid.NewGuid(), "PARCEL", wrongPaymentId.Id, 30_000, Now.AddMinutes(10),
                    "https://sandbox.vnpayment.vn/wrong-payment"),
                LookupItem(wrongAmount.DepositPaymentId!.Value, "PARCEL", wrongAmount.Id, 39_999,
                    Now.AddMinutes(10), "https://sandbox.vnpayment.vn/wrong-amount"),
                LookupItem(lateFinal.BalancePaymentId!.Value, "PARCEL_ADDITIONAL", lateFinal.Id, 50_000,
                    Now.AddMinutes(16), "https://sandbox.vnpayment.vn/late-final"),
            ]);
        var handler = CreateHandler(
            Substitute.For<IBookingServiceClient>(),
            repository,
            TripClientUnavailable(),
            paymentLookup);

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().OnlyContain(item => item.PaymentRedirectUrl == null);
        await paymentLookup.Received(1).LookupAsync(
            userId,
            Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParcelBranch_ExcludesLegacyAdditionalAndExpiredDeadlineWithoutPaymentCall()
    {
        var userId = Guid.NewGuid();
        var legacyAdditional = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_ADDITIONAL_PAYMENT,
            depositPaymentId: Guid.NewGuid(),
            balancePaymentId: Guid.NewGuid(),
            depositRequired: 40_000,
            balanceRequired: 50_000,
            latestCheckInAt: Now.AddMinutes(20),
            finalPaymentDeadline: Now.AddMinutes(20));
        var expiredDeposit = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_PAYMENT,
            depositPaymentId: Guid.NewGuid(),
            depositRequired: 40_000,
            latestCheckInAt: Now);
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        var handler = CreateHandler(
            Substitute.For<IBookingServiceClient>(),
            RepositoryWithPage(userId, legacyAdditional, expiredDeposit),
            TripClientUnavailable(),
            paymentLookup);

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().OnlyContain(item => item.PaymentRedirectUrl == null);
        await paymentLookup.DidNotReceiveWithAnyArgs().LookupAsync(default, default!, default);
    }

    [Fact]
    public async Task ParcelBranch_WhenPaymentLookupFails_FailsOpen()
    {
        var userId = Guid.NewGuid();
        var parcel = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_PAYMENT,
            depositPaymentId: Guid.NewGuid(),
            depositRequired: 40_000,
            latestCheckInAt: Now.AddMinutes(20));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        paymentLookup.LookupAsync(
                userId,
                Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PaymentRedirectLookupItem>>>(_ =>
                throw new HttpRequestException("unavailable"));
        var handler = CreateHandler(
            Substitute.For<IBookingServiceClient>(),
            RepositoryWithPage(userId, parcel),
            TripClientUnavailable(),
            paymentLookup);

        var result = await handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().ContainSingle().Which.PaymentRedirectUrl.Should().BeNull();
    }

    [Fact]
    public async Task ParcelBranch_WhenCallerCancelsPaymentLookup_PropagatesCancellation()
    {
        var userId = Guid.NewGuid();
        var parcel = CreateSettlementParcel(
            userId,
            ParcelStatus.PENDING_PAYMENT,
            depositPaymentId: Guid.NewGuid(),
            depositRequired: 40_000,
            latestCheckInAt: Now.AddMinutes(20));
        var paymentLookup = Substitute.For<IPaymentRedirectLookupClient>();
        using var source = new CancellationTokenSource();
        paymentLookup.LookupAsync(
                userId,
                Arg.Any<IReadOnlyCollection<PaymentRedirectLookupReference>>(),
                source.Token)
            .Returns<Task<IReadOnlyList<PaymentRedirectLookupItem>>>(_ =>
            {
                source.Cancel();
                throw new OperationCanceledException(source.Token);
            });
        var handler = CreateHandler(
            Substitute.For<IBookingServiceClient>(),
            RepositoryWithPage(userId, parcel),
            TripClientUnavailable(),
            paymentLookup);

        var action = () => handler.Handle(
            new GetPassengerHistoryQuery(userId, "PARCEL", null, null, null, 1, 20),
            source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Query_UsesReadOnlyCqrsMarker()
    {
        var query = new GetPassengerHistoryQuery(Guid.NewGuid(), "PARCEL", null, null, null, 1, 20);

        query.Should().BeAssignableTo<IQuery<PagedResult<PassengerHistoryItemDto>>>();
    }

    [Fact]
    public void Dtos_AlwaysSerializeRedirectButSentHistoryNeverExposesSettlementInternals()
    {
        var passenger = new PassengerHistoryItemDto(
            "PARCEL",
            Guid.NewGuid(),
            "VR-PCL-HISTORY",
            Guid.NewGuid(),
            "PENDING_PAYMENT",
            Now,
            40_000,
            null,
            null,
            null,
            null,
            null,
            null);
        var sent = new SentParcelHistoryItemDto(
            Guid.NewGuid(),
            "VR-PCL-HISTORY",
            Guid.NewGuid(),
            "PENDING_PAYMENT",
            Now,
            40_000,
            null,
            null,
            null,
            null,
            null,
            "Recipient",
            "SMALL",
            null,
            "TERMINAL_PICKUP");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        using var passengerJson = JsonDocument.Parse(JsonSerializer.Serialize(passenger, options));
        using var sentJson = JsonDocument.Parse(JsonSerializer.Serialize(sent, options));

        passengerJson.RootElement.TryGetProperty("paymentRedirectUrl", out var redirect).Should().BeTrue();
        redirect.ValueKind.Should().Be(JsonValueKind.Null);
        foreach (var forbidden in new[]
                 {
                     "depositPaymentId",
                     "balancePaymentId",
                     "latestCheckInAt",
                     "finalPaymentDeadline",
                     "depositRequiredVnd",
                     "depositPaidVnd",
                     "balanceRequiredVnd",
                     "balancePaidVnd",
                 })
        {
            sentJson.RootElement.TryGetProperty(forbidden, out _).Should().BeFalse();
        }
    }

    [Fact]
    public void TicketHistoryDetailsAlwaysSerializesNullableVehicle()
    {
        var details = new TicketHistoryDetailsDto(null, null, "Route", []);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(details, options));

        json.RootElement.TryGetProperty("vehicle", out var vehicle).Should().BeTrue();
        vehicle.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void PassengerHistoryVehicleAlwaysSerializesNullableVehicleType()
    {
        var vehicle = new PassengerHistoryVehicleDto("51B-123.45");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(vehicle, options));

        json.RootElement.TryGetProperty("vehicleType", out var vehicleType).Should().BeTrue();
        vehicleType.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("ALL", null)]
    [InlineData("TICKET", "IN_TRANSIT")]
    [InlineData("PARCEL", "CONFIRMED")]
    public void Validator_RejectsUnsupportedTypeOrBranchStatus(string type, string? status)
    {
        var validator = new GetPassengerHistoryQueryValidator();

        var result = validator.TestValidate(
            new GetPassengerHistoryQuery(Guid.NewGuid(), type, status, null, null, 1, 20));

        result.IsValid.Should().BeFalse();
    }

    private static GetPassengerHistoryQueryHandler CreateHandler(
        IBookingServiceClient bookings,
        IParcelRepository parcels,
        ITripServiceClient trips,
        IPaymentRedirectLookupClient paymentLookup)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new GetPassengerHistoryQueryHandler(
            bookings,
            new SentParcelHistoryReader(parcels, trips),
            paymentLookup,
            clock);
    }

    private static IParcelRepository RepositoryWithPage(Guid userId, params ParcelEntity[] parcels)
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.ListSentByUserIdAsync(
                userId,
                null,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create(parcels, 1, 20, parcels.Length));
        return repository;
    }

    private static ITripServiceClient TripClientUnavailable()
    {
        var trips = Substitute.For<ITripServiceClient>();
        trips.GetTripParcelSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.TransportError, null, "unavailable"));
        return trips;
    }

    private static ParcelEntity CreateSettlementParcel(
        Guid userId,
        ParcelStatus status,
        Guid? depositPaymentId = null,
        Guid? balancePaymentId = null,
        long depositRequired = 0,
        long depositPaid = 0,
        long balanceRequired = 0,
        long balancePaid = 0,
        DateTimeOffset? latestCheckInAt = null,
        DateTimeOffset? finalPaymentDeadline = null)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            $"VR-PCL-{Guid.NewGuid():N}",
            userId,
            null,
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            ParcelSizeCategory.SMALL,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(depositRequired));
        var latestCheckIn = latestCheckInAt ?? Now.AddMinutes(20);
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.SMALL,
            Money.FromRaw(Math.Max(depositRequired + balanceRequired, 1)),
            Money.Zero,
            Money.FromRaw(Math.Max(depositRequired + balanceRequired, 1)),
            20m,
            Money.FromRaw(depositRequired),
            Money.FromRaw(1_000),
            Money.Zero,
            6000m,
            latestCheckIn.AddMinutes(10),
            latestCheckIn);
        Set(parcel, nameof(ParcelEntity.Status), status);
        Set(parcel, nameof(ParcelEntity.DepositPaymentId), depositPaymentId);
        Set(parcel, nameof(ParcelEntity.BalancePaymentId), balancePaymentId);
        Set(parcel, nameof(ParcelEntity.DepositPaidVnd), Money.FromRaw(depositPaid));
        Set(parcel, nameof(ParcelEntity.BalanceRequiredVnd), Money.FromRaw(balanceRequired));
        Set(parcel, nameof(ParcelEntity.BalancePaidVnd), Money.FromRaw(balancePaid));
        Set(parcel, nameof(ParcelEntity.FinalPaymentDeadline), finalPaymentDeadline);
        return parcel;
    }

    private static PaymentRedirectLookupItem LookupItem(
        Guid paymentId,
        string referenceType,
        Guid referenceId,
        long amount,
        DateTimeOffset dueAt,
        string url)
        => new(paymentId, referenceType, referenceId, amount, dueAt, url);

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property!.SetValue(target, value);
    }
}
