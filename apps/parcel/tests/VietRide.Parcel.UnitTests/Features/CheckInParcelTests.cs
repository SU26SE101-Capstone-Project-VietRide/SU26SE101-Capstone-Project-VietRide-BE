using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.CheckIn;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class CheckInParcelTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid AssistantUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
    private static readonly string CheckInPhotoUrl =
        $"https://storage.googleapis.com/vietride.appspot.com/parcel-ops/{OperatorId:D}/{AssistantUserId:D}/{ParcelId:D}/check-in.webp";

    [Fact]
    public async Task Handle_AssignedAssistantBeforeDeadline_ChecksInReservedParcel()
    {
        var repository = Substitute.For<IParcelRepository>();
        var trip = Substitute.For<ITripServiceClient>();
        var clock = Clock();
        var parcel = CreateReservedParcel(Now.AddMinutes(10));
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        trip.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.TryCheckInAsync(
                ParcelId,
                TripId,
                parcel.ParcelCode,
                AssistantUserId,
                Arg.Is<IReadOnlyCollection<string>?>(urls =>
                    urls != null && urls.SequenceEqual(new[] { CheckInPhotoUrl })),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.CHECKED_IN));

        var result = await new CheckInParcelCommandHandler(repository, trip, clock).Handle(
            new CheckInParcelCommand(
                ParcelId,
                TripId,
                parcel.ParcelCode,
                new[] { $"  {CheckInPhotoUrl}  " },
                AssistantUserId,
                OperatorId),
            CancellationToken.None);

        result.Status.Should().Be(nameof(ParcelStatus.CHECKED_IN));
        result.CheckedInAt.Should().Be(Now);
        result.LatestCheckInAt.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public async Task Handle_UnassignedAssistant_IsForbidden()
    {
        var repository = Substitute.For<IParcelRepository>();
        var trip = Substitute.For<ITripServiceClient>();
        trip.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var act = () => new CheckInParcelCommandHandler(repository, trip, Clock()).Handle(
            new CheckInParcelCommand(
                ParcelId,
                TripId,
                "VRP-20260727-TEST0001",
                null,
                AssistantUserId,
                OperatorId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await repository.DidNotReceive().TryCheckInAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyCollection<string>?>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AtDeadline_IsRejectedWithoutTransition()
    {
        var repository = Substitute.For<IParcelRepository>();
        var trip = Substitute.For<ITripServiceClient>();
        var parcel = CreateReservedParcel(Now);
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        trip.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));

        var act = () => new CheckInParcelCommandHandler(repository, trip, Clock()).Handle(
            new CheckInParcelCommand(
                ParcelId,
                TripId,
                parcel.ParcelCode,
                null,
                AssistantUserId,
                OperatorId),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("PARCEL_CHECK_IN_CLOSED");
        await repository.DidNotReceive().TryCheckInAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyCollection<string>?>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    private static ParcelEntity CreateReservedParcel(DateTimeOffset latestCheckInAt)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260727-TEST0001",
            SenderUserId,
            null,
            "Receiver",
            PhoneNumber.Normalize("+84912345678"),
            null,
            OperatorId,
            TripId,
            null,
            null,
            null,
            null,
            ParcelSizeCategory.SMALL,
            estimatedLengthCm: 10m,
            estimatedWidthCm: 10m,
            estimatedHeightCm: 10m,
            estimatedWeightKg: 2m,
            estimatedVolumeM3: 0.001m,
            estimatedDimWeightKg: 0.17m,
            estimatedChargeableWeightKg: 2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            totalPrice: Money.FromRaw(2_000),
            depositPercent: 20m,
            depositAmount: Money.FromRaw(400));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Id), ParcelId);
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.SMALL,
            Money.FromRaw(2_000),
            Money.Zero,
            Money.FromRaw(2_000),
            20m,
            Money.FromRaw(400),
            Money.FromRaw(1_000),
            Money.Zero,
            ParcelCargoCalculator.DefaultDimWeightFactor,
            latestCheckInAt.AddMinutes(10),
            latestCheckInAt);
        SetPrivateProperty(parcel, nameof(ParcelEntity.Status), ParcelStatus.RESERVED);
        return parcel;
    }

    private static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private static ParcelPaymentTransitionSnapshot Snapshot(ParcelStatus status)
        => new(
            ParcelId,
            "VRP-20260727-TEST0001",
            status,
            DepositAmount: 400,
            AdditionalAmount: 0,
            OperatorId,
            TripId,
            BookingId: null,
            SenderUserId,
            ParcelSizeCategory.SMALL,
            AdditionalPaymentId: null);

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, value);
}
