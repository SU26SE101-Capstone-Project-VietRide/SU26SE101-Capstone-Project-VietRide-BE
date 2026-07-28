using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.QrScan;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features.Parcels.QrScan;

public sealed class ScanParcelCodeForTripQueryHandlerTests
{
    private const string ParcelCode = "VR-PCL-20260728-ABCDEFGH";
    private const string PhotoUrl = "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg";
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid AssistantUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();

    [Fact]
    public async Task Handle_AssignedAssistantAndMatchingParcel_ReturnsReadOnlyScanResult()
    {
        var parcel = CreateParcel();
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.FindByParcelCodeAsync(ParcelCode, Arg.Any<CancellationToken>())
            .Returns(parcel);

        var result = await new ScanParcelCodeForTripQueryHandler(repository, tripClient).Handle(
            new ScanParcelCodeForTripQuery(TripId, ParcelCode, AssistantUserId, OperatorId),
            CancellationToken.None);

        result.Should().BeEquivalentTo(new ScanParcelCodeForTripResult(
            parcel.Id,
            ParcelCode,
            parcel.Status.ToString(),
            TripId,
            parcel.RecipientName,
            parcel.SizeCategory.ToString(),
            parcel.PhotoUrl));
    }

    [Fact]
    public async Task Handle_DeniedAssistant_DoesNotLookupParcel()
    {
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var action = () => new ScanParcelCodeForTripQueryHandler(repository, tripClient).Handle(
            new ScanParcelCodeForTripQuery(TripId, ParcelCode, AssistantUserId, OperatorId),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        await repository.DidNotReceive().FindByParcelCodeAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ParcelFromAnotherTripOrOperator_ReturnsNotFound()
    {
        var parcel = CreateParcel();
        var otherOperatorId = Guid.NewGuid();
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                otherOperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        repository.FindByParcelCodeAsync(ParcelCode, Arg.Any<CancellationToken>())
            .Returns(parcel);

        var action = () => new ScanParcelCodeForTripQueryHandler(repository, tripClient).Handle(
            new ScanParcelCodeForTripQuery(TripId, ParcelCode, AssistantUserId, otherOperatorId),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "PARCEL_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_TripServiceUnavailable_DoesNotLookupParcel()
    {
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(
                TripCrewAuthorizationOutcomeKind.TransportError,
                "Trip service unavailable."));

        var action = () => new ScanParcelCodeForTripQueryHandler(repository, tripClient).Handle(
            new ScanParcelCodeForTripQuery(TripId, ParcelCode, AssistantUserId, OperatorId),
            CancellationToken.None);

        await action.Should().ThrowAsync<ParcelDependencyUnavailableException>()
            .Where(exception => exception.ErrorCode == "TRIP_SERVICE_UNAVAILABLE");
        await repository.DidNotReceive().FindByParcelCodeAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("VRP-001")]
    [InlineData("VR-PCL-20260728-INVALID0")]
    [InlineData("QR://VR-PCL-20260728-ABCDEFGH")]
    public void Validator_RejectsMalformedParcelCode(string parcelCode)
    {
        var result = new ScanParcelCodeForTripQueryValidator().Validate(
            new ScanParcelCodeForTripQuery(TripId, parcelCode, AssistantUserId, OperatorId));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(ParcelCode)]
    [InlineData("VRP-20260728-ABCDEFGH")]
    public void Validator_AcceptsCurrentAndLegacyParcelCodes(string parcelCode)
    {
        var result = new ScanParcelCodeForTripQueryValidator().Validate(
            new ScanParcelCodeForTripQuery(TripId, parcelCode, AssistantUserId, OperatorId));

        result.IsValid.Should().BeTrue();
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            ParcelCode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Nguyen Van A",
            PhoneNumber.Normalize("0900000000"),
            null,
            OperatorId,
            TripId,
            null,
            null,
            "Goi hang nho",
            PhotoUrl,
            ParcelSizeCategory.SMALL,
            2.5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));
}
