using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.Detail;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.Detail;

public sealed class GetParcelDetailQueryHandlerTests
{
    private const string PhotoUrl = "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg";
    private const string CheckInPhotoUrl = "https://storage.googleapis.com/vietride.appspot.com/check-in.jpg";
    private const string DeliveryPhotoUrl = "https://storage.googleapis.com/vietride.appspot.com/delivery.jpg";

    [Theory]
    [InlineData("sender")]
    [InlineData("recipient")]
    [InlineData("operator")]
    public async Task Handle_AuthorizedCaller_ReturnsPhotoUrl(string callerType)
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.GetTripParcelSnapshotAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.TransportError, null, "unavailable"));
        var userId = callerType switch
        {
            "sender" => parcel.SenderUserId,
            "recipient" => parcel.RecipientUserId,
            _ => null
        };
        Guid? operatorId = callerType == "operator" ? parcel.OperatorId : null;

        var result = await new GetParcelDetailQueryHandler(repository, tripClient)
            .Handle(new GetParcelDetailQuery(parcel.Id, userId, operatorId), default);

        result.PhotoUrl.Should().Be(PhotoUrl);
        result.CheckInPhotoUrls.Should().Equal(CheckInPhotoUrl);
        result.DeliveryPhotoUrls.Should().Equal(DeliveryPhotoUrl);
        result.BookingId.Should().Be(parcel.BookingId);
        result.SettlementPolicyVersion.Should().Be(2);
        result.EstimatedSizeCategory.Should().Be("SMALL");
        result.EstimatedGrossPriceVnd.Should().Be(50_000);
        result.EstimatedTotalPriceVnd.Should().Be(50_000);
        result.DepositRequiredVnd.Should().Be(10_000);
    }

    [Fact]
    public async Task Handle_UnrelatedCaller_RemainsForbidden()
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.GetByIdAsync(parcel.Id, Arg.Any<CancellationToken>()).Returns(parcel);

        var action = () => new GetParcelDetailQueryHandler(repository, tripClient)
            .Handle(new GetParcelDetailQuery(parcel.Id, Guid.NewGuid(), null), default);

        await action.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
    }

    private static ParcelEntity CreateParcel()
    {
        var bookingId = Guid.NewGuid();
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-PCL-DETAIL",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            bookingId,
            "Fragile",
            PhotoUrl,
            ParcelSizeCategory.SMALL,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(50_000));
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.SMALL,
            Money.FromRaw(50_000),
            Money.Zero,
            Money.FromRaw(50_000),
            20m,
            Money.FromRaw(10_000),
            Money.FromRaw(50_000),
            Money.Zero,
            6000m,
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(1));
        SetPrivateProperty(parcel, nameof(ParcelEntity.CheckInPhotoUrls), new[] { CheckInPhotoUrl });
        SetPrivateProperty(parcel, nameof(ParcelEntity.DeliveryPhotoUrls), new[] { DeliveryPhotoUrl });
        return parcel;
    }

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, value);
}
