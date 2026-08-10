using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.InternalDetail;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class GetParcelPaymentContextQueryHandlerTests
{
    private static readonly Guid SenderId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ResolvableParcel_ReturnsParcelCodeAsReferenceCode()
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);

        var result = await new GetParcelPaymentContextQueryHandler(repository).Handle(
            new GetParcelPaymentContextQuery("PARCEL", parcel.Id),
            CancellationToken.None);

        result.CanBackfill.Should().BeTrue();
        result.Allocations.Should().ContainSingle();
        result.Allocations[0].ReferenceCode.Should().Be(parcel.ParcelCode);
    }

    private static ParcelEntity CreateParcel()
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260810-BACKFILL01",
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
            Money.FromRaw(20_000));
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.SMALL,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            20m,
            Money.FromRaw(20_000),
            Money.FromRaw(1_000),
            Money.Zero,
            6000m,
            Now.AddHours(2),
            Now.AddHours(1));
        return parcel;
    }
}
