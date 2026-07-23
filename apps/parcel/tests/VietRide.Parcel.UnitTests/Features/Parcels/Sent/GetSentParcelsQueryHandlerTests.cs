using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Application.Features.Parcels.Sent;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.Sent;

public sealed class GetSentParcelsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsExistingPhotoUrl()
    {
        const string photoUrl = "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg";
        var userId = Guid.NewGuid();
        var parcel = ParcelEntity.CreatePendingPayment(
            "VR-PCL-SENT",
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
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.ListSentByUserIdAsync(
                userId,
                null,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        tripClient.GetTripParcelSnapshotAsync(parcel.TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(TripSnapshotOutcomeKind.TransportError, null, "unavailable"));
        var handler = new GetSentParcelsQueryHandler(new SentParcelHistoryReader(repository, tripClient));

        var result = await handler.Handle(
            new GetSentParcelsQuery(userId, null, null, null, 1, 20),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].PhotoUrl.Should().Be(photoUrl);
    }
}
