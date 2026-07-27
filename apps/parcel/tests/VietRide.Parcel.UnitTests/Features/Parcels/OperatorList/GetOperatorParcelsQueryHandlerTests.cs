using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.OperatorList;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.OperatorList;

public sealed class GetOperatorParcelsQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsOnlyRepositoryPageForAuthenticatedOperatorScope()
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        repository.ListByOperatorAsync(
                OperatorId,
                ParcelStatus.PENDING_OPERATOR_REVIEW,
                TripId,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));

        var result = await new GetOperatorParcelsQueryHandler(repository).Handle(
            new GetOperatorParcelsQuery(
                OperatorId,
                "pending_operator_review",
                TripId,
                null,
                1,
                20),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        var item = result.Items[0];
        item.ParcelId.Should().Be(parcel.Id);
        item.ParcelCode.Should().Be(parcel.ParcelCode);
        item.Status.Should().Be("PENDING_OPERATOR_REVIEW");
        item.TripId.Should().Be(TripId);
        item.SenderUserId.Should().Be(parcel.SenderUserId);
        item.RecipientName.Should().Be(parcel.RecipientName);
        item.EstimatedSizeCategory.Should().Be("EXTRA_LARGE");
        item.EstimatedChargeableWeightKg.Should().Be(parcel.EstimatedChargeableWeightKg);
        item.DepositRequiredVnd.Should().Be(parcel.DepositRequiredVnd.Amount);
        item.PendingActionType.Should().BeNull();
        item.PhotoUrl.Should().Be(parcel.PhotoUrl);

        await repository.Received(1).ListByOperatorAsync(
            OperatorId,
            ParcelStatus.PENDING_OPERATOR_REVIEW,
            TripId,
            null,
            1,
            20,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("UNKNOWN", null, 1, 20)]
    [InlineData("999", null, 1, 20)]
    [InlineData(null, "UNKNOWN", 1, 20)]
    [InlineData(null, "999", 1, 20)]
    [InlineData(null, null, 0, 20)]
    [InlineData(null, null, 1, 0)]
    [InlineData(null, null, 1, 101)]
    public void Validator_RejectsInvalidFiltersOrPagination(
        string? status,
        string? pendingActionType,
        int page,
        int pageSize)
    {
        var result = new GetOperatorParcelsQueryValidator().Validate(
            new GetOperatorParcelsQuery(
                OperatorId,
                status,
                null,
                pendingActionType,
                page,
                pageSize));

        result.IsValid.Should().BeFalse();
    }

    private static ParcelEntity CreateParcel()
    {
        var parcel = ParcelEntity.CreatePendingOperatorReview(
            "VR-PCL-OPERATOR-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Nguyen Van A",
            PhoneNumber.Normalize("0900000000"),
            null,
            OperatorId,
            TripId,
            null,
            null,
            "Hang can duyet",
            "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.webp",
            ParcelSizeCategory.EXTRA_LARGE,
            50m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(10_000));
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.EXTRA_LARGE,
            Money.FromRaw(50_000),
            Money.Zero,
            Money.FromRaw(50_000),
            20m,
            Money.FromRaw(10_000),
            Money.FromRaw(1_000),
            Money.FromRaw(1_000),
            6000m,
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(1));
        return parcel;
    }
}
