using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.Review;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ReviewParcelTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();

    private static ParcelEntity CreatePendingReviewParcel()
    {
        return ParcelEntity.CreatePendingOperatorReview(
            "VRP-001", SenderUserId, Guid.NewGuid(), "Recipient",
            PhoneNumber.Normalize("+84912345678"), "r@example.com",
            OperatorId, TripId, Guid.NewGuid(), null, "Item", "",
            ParcelSizeCategory.EXTRA_LARGE, 10m,
            ParcelDeliveryMethod.TERMINAL_PICKUP, Money.FromRaw(200_000));
    }

    [Fact]
    public async Task ReviewParcel_Approve_Success()
    {
        var parcel = CreatePendingReviewParcel();
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryApproveReviewAsync(ParcelId, OperatorId, Money.FromRaw(200_000), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(ParcelId, "VRP-001", ParcelStatus.PENDING_PAYMENT,
                200_000, 0, OperatorId, TripId, null, SenderUserId, ParcelSizeCategory.EXTRA_LARGE, null));

        var paymentClient = Substitute.For<IPaymentServiceClient>();
        paymentClient.ChargeParcelPaymentAsync("PARCEL", ParcelId, SenderUserId, 200_000,
                "VNPAY", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChargeOutcome(ChargeOutcomeKind.Success,
                new ChargeResult(Guid.NewGuid(), "SUCCEEDED", null), null));

        var handler = new ReviewParcelCommandHandler(repo, paymentClient);
        var result = await handler.Handle(new ReviewParcelCommand(
            ParcelId, OperatorId, OperatorId, "APPROVED", 200_000, null, "VNPAY"), default);

        result.Status.Should().Be("PENDING_PAYMENT");
        result.DepositAmount.Should().Be(200_000);
    }

    [Fact]
    public async Task ReviewParcel_Reject_Success()
    {
        var parcel = CreatePendingReviewParcel();
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryRejectReviewAsync(ParcelId, OperatorId, "Overweight", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(ParcelId, "VRP-001", ParcelStatus.REJECTED,
                0, 0, OperatorId, TripId, null, SenderUserId, ParcelSizeCategory.EXTRA_LARGE, null));

        var handler = new ReviewParcelCommandHandler(repo,
            Substitute.For<IPaymentServiceClient>());
        var result = await handler.Handle(new ReviewParcelCommand(
            ParcelId, OperatorId, OperatorId, "REJECTED", null, "Overweight", "VNPAY"), default);

        result.Status.Should().Be("REJECTED");
    }

    [Fact]
    public async Task ReviewParcel_ParcelNotFound_Throws()
    {
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns((ParcelEntity?)null);

        var handler = new ReviewParcelCommandHandler(repo,
            Substitute.For<IPaymentServiceClient>());
        var act = () => handler.Handle(new ReviewParcelCommand(
            ParcelId, OperatorId, OperatorId, "APPROVED", 200_000, null, "VNPAY"), default);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(e => e.ErrorCode == "PARCEL_NOT_FOUND");
    }
}
