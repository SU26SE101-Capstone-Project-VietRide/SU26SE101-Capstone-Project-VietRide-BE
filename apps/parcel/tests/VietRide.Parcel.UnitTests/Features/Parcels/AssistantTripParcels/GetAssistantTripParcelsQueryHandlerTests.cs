using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.AssistantTripParcels;

public sealed class GetAssistantTripParcelsQueryHandlerTests
{
    private const string PhotoUrl = "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg";
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();

    [Fact]
    public async Task Handle_AuthorizedAssistant_ReturnsMappedPagedParcels()
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorAsync(TripId, OperatorId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));

        var result = await new GetAssistantTripParcelsQueryHandler(repository, tripClient)
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        result.Items.Should().ContainSingle();
        result.Items[0].Should().BeEquivalentTo(new AssistantTripParcelResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.RecipientName,
            parcel.RecipientPhone.ToString(),
            parcel.DropoffStopId,
            parcel.SizeCategory.ToString(),
            parcel.EstimatedWeightKg,
            parcel.Description,
            parcel.PhotoUrl));
        await repository.Received(1).ListByTripAndOperatorAsync(TripId, OperatorId, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeniedAssistant_DoesNotQueryParcels()
    {
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var action = () => new GetAssistantTripParcelsQueryHandler(repository, tripClient)
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        await action.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        await repository.DidNotReceive().ListByTripAndOperatorAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Validator_RejectsInvalidPagination(int page, int pageSize)
    {
        var result = new GetAssistantTripParcelsQueryValidator()
            .Validate(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, page, pageSize));

        result.IsValid.Should().BeFalse();
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            "VR-PCL-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Nguyen Van A",
            PhoneNumber.Normalize("0900000000"),
            null,
            OperatorId,
            TripId,
            Guid.NewGuid(),
            null,
            "Goi hang nho",
            PhotoUrl,
            ParcelSizeCategory.SMALL,
            2.5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
}
