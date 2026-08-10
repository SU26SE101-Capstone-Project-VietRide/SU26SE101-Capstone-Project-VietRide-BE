using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.DepositPayment;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelDepositPaymentTests
{
    private static readonly Guid SenderId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartDeposit_ReservesEstimatedCargoAndCreatesFifteenMinutePayment()
    {
        var parcel = CreateParcel(depositRequired: 20_000);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryAssignDepositPaymentIdAsync(
                parcel.Id, Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var trip = SuccessfulTripClient();
        var payment = Substitute.For<IPaymentServiceClient>();
        var paymentId = Guid.NewGuid();
        payment.ChargeParcelPaymentAsync(
                "PARCEL",
                parcel.Id,
                SenderId,
                20_000,
                "VNPAY",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<PaymentContextSnapshot?>(),
                Arg.Any<DateTimeOffset?>())
            .Returns(call => new ChargeOutcome(
                ChargeOutcomeKind.Success,
                new ChargeResult(paymentId, "PENDING_REDIRECT", "https://pay", call.ArgAt<DateTimeOffset?>(8)),
                null));

        var result = await CreateHandler(repository, trip, payment).Handle(
            new StartParcelDepositPaymentCommand(
                parcel.Id,
                SenderId,
                "VNPAY",
                Guid.NewGuid().ToString("D")),
            default);

        result.Status.Should().Be(ParcelStatus.PENDING_PAYMENT.ToString());
        result.DepositPaymentId.Should().Be(paymentId);
        result.PaymentDueAt.Should().Be(Now.AddMinutes(15));
        await payment.Received(1).ChargeParcelPaymentAsync(
            "PARCEL",
            parcel.Id,
            SenderId,
            20_000,
            "VNPAY",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>(),
            Arg.Is<PaymentContextSnapshot>(context =>
                context.Allocations.Count == 1
                && context.Allocations[0].ReferenceCode == parcel.ParcelCode),
            Arg.Any<DateTimeOffset?>());
        await trip.Received(1).ReserveCargoAsync(
            TripId,
            parcel.Id,
            parcel.EstimatedWeightKg,
            parcel.EstimatedVolumeM3,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartDeposit_ZeroAmountReservesAndBecomesReservedWithoutPayment()
    {
        var parcel = CreateParcel(depositRequired: 0);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryActivateZeroDepositAsync(
                parcel.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(
                parcel.Id,
                parcel.ParcelCode,
                ParcelStatus.RESERVED,
                0,
                0,
                OperatorId,
                TripId,
                null,
                SenderId,
                ParcelSizeCategory.SMALL,
                null));
        var payment = Substitute.For<IPaymentServiceClient>();

        var result = await CreateHandler(repository, SuccessfulTripClient(), payment).Handle(
            new StartParcelDepositPaymentCommand(
                parcel.Id,
                SenderId,
                "WALLET",
                Guid.NewGuid().ToString("D")),
            default);

        result.Status.Should().Be(ParcelStatus.RESERVED.ToString());
        await payment.DidNotReceiveWithAnyArgs().ChargeParcelPaymentAsync(
            default!, default, default, default, default!, default!, default, default, default);
    }

    private static StartParcelDepositPaymentCommandHandler CreateHandler(
        IParcelRepository repository,
        ITripServiceClient trip,
        IPaymentServiceClient payment)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new StartParcelDepositPaymentCommandHandler(
            repository,
            trip,
            payment,
            Substitute.For<IBookingServiceClient>(),
            Substitute.For<IUnitOfWork>(),
            clock);
    }

    private static ITripServiceClient SuccessfulTripClient()
    {
        var trip = Substitute.For<ITripServiceClient>();
        trip.ReserveCargoAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        return trip;
    }

    private static ParcelEntity CreateParcel(long depositRequired)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-TEST",
            SenderId,
            null,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            null,
            OperatorId,
            TripId,
            null,
            null,
            "Goods",
            null,
            ParcelSizeCategory.SMALL,
            10m,
            10m,
            10m,
            3.2m,
            0.001m,
            0.17m,
            3.2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000),
            20m,
            Money.FromRaw(depositRequired));
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.SMALL,
            Money.FromRaw(100_000),
            Money.FromRaw(depositRequired == 0 ? 100_000 : 0),
            Money.FromRaw(depositRequired == 0 ? 0 : 100_000),
            20m,
            Money.FromRaw(depositRequired),
            Money.FromRaw(1_000),
            Money.Zero,
            6000m,
            Now.AddHours(2),
            Now.AddHours(1));
        return parcel;
    }
}
