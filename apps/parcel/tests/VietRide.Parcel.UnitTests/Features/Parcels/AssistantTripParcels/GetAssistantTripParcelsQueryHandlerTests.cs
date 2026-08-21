using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
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
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.ListByTripAndOperatorFilteredAsync(
                TripId,
                OperatorId,
                null,
                null,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelEntity>.Create([parcel], 1, 20, 1));
        repository.GetAssistantManifestCountsAsync(TripId, OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new AssistantParcelManifestCounts(1, 0, 0, 0, 0, 0, 0));
        screenModels.BuildAsync(
                Arg.Any<IReadOnlyCollection<ParcelEntity>>(),
                UserId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ParcelScreenReadModel>
            {
                [parcel.Id] = CreateScreen(parcel),
            });

        var result = await new GetAssistantTripParcelsQueryHandler(repository, tripClient, screenModels)
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        result.Items.Should().ContainSingle();
        result.Items[0].Should().BeEquivalentTo(new
        {
            ParcelId = parcel.Id,
            parcel.ParcelCode,
            Status = parcel.Status.ToString(),
            parcel.RecipientName,
            RecipientPhone = parcel.RecipientPhone.ToString(),
            parcel.DropoffStopId,
            SizeCategory = parcel.SizeCategory.ToString(),
            EstimatedSizeCategory = parcel.EstimatedSizeCategory.ToString(),
            ActualSizeCategory = parcel.ActualSizeCategory?.ToString(),
            parcel.EstimatedWeightKg,
            parcel.ActualWeightKg,
            BalanceRequiredVnd = parcel.BalanceRequiredVnd.Amount,
            BalancePaidVnd = parcel.BalancePaidVnd.Amount,
            parcel.FinalPaymentDeadline,
            parcel.Description,
            parcel.PhotoUrl,
        });
        await repository.Received(1).ListByTripAndOperatorFilteredAsync(
            TripId,
            OperatorId,
            null,
            null,
            null,
            null,
            1,
            20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeniedAssistant_DoesNotQueryParcels()
    {
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var screenModels = Substitute.For<IParcelReliabilityReadModelService>();
        tripClient.AuthorizeAssistantForTripAsync(TripId, UserId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var action = () => new GetAssistantTripParcelsQueryHandler(repository, tripClient, screenModels)
            .Handle(new GetAssistantTripParcelsQuery(TripId, UserId, OperatorId, 1, 20), default);

        await action.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        await repository.DidNotReceive().ListByTripAndOperatorFilteredAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<ParcelStatus?>(),
            Arg.Any<bool?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
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

    private static ParcelScreenReadModel CreateScreen(ParcelEntity parcel)
        => new(
            new ReliabilityParcelSummaryResponse(
                parcel.Id,
                parcel.ParcelCode,
                parcel.Status.ToString(),
                parcel.Description,
                parcel.PhotoUrl,
                parcel.Quantity,
                parcel.DeclaredValueVnd),
            new ReliabilityOperatorResponse(OperatorId, "Operator", null, null),
            new ReliabilityTripResponse(
                TripId,
                "BOARDING",
                null,
                null,
                null,
                null,
                []),
            null,
            new ReliabilityLocationResponse("ROUTE_STOP", parcel.DropoffStopId, null),
            new ParcelReliabilitySummaryResponse(null, null, null, null, []));
}
