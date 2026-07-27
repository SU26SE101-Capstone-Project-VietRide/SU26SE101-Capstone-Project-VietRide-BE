using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.RecordRefund;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class RecordParcelRefundedTests
{
    [Fact]
    public async Task WalletCreditForParcelRefund_IncrementsCanonicalRefundedAmount()
    {
        var now = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryRecordRefundedAmountAsync(
                parcel.Id,
                Money.Zero,
                Money.FromRaw(1_000),
                now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var handled = await new RecordParcelRefundedCommandHandler(repository, clock).Handle(
            new RecordParcelRefundedCommand(
                parcel.Id,
                parcel.SenderUserId,
                1_000,
                "PARCEL_REFUND"),
            CancellationToken.None);

        handled.Should().BeTrue();
        await repository.Received(1).TryRecordRefundedAmountAsync(
            parcel.Id,
            Money.Zero,
            Money.FromRaw(1_000),
            now,
            Arg.Any<CancellationToken>());
    }

    private static ParcelEntity CreateParcel()
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260727-REFUND01",
            Guid.NewGuid(),
            null,
            "Receiver",
            PhoneNumber.Normalize("+84912345678"),
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
            Money.FromRaw(2_000));
        SetPrivateProperty(parcel, nameof(ParcelEntity.RefundDueVnd), Money.FromRaw(1_000));
        return parcel;
    }

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, value);
}
