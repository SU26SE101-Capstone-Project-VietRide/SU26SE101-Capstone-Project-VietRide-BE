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
        => ParcelEntity.CreatePendingPayment(
            "VR-PCL-DETAIL",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("0900000000"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Fragile",
            PhotoUrl,
            ParcelSizeCategory.SMALL,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(50_000));
}
