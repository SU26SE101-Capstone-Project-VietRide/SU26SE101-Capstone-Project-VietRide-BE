using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ListParcelIncidentsApprovalFilterTests
{
    [Fact]
    public async Task Handle_PendingApprovalFilter_IsAppliedBeforePagination()
    {
        var operatorId = Guid.NewGuid();
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.SearchIncidentsByOperatorAsync(
                operatorId,
                null,
                null,
                null,
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                null,
                null,
                ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL,
                null,
                null,
                Arg.Any<DateTimeOffset>(),
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ParcelIncident>.Create([], 1, 20, 0));

        var result = await CreateHandler(reliability).Handle(
            new ListParcelIncidentsQuery(
                operatorId,
                null,
                null,
                null,
                null,
                null,
                null,
                "PENDING_APPROVAL",
                null,
                null,
                1,
                20),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
        await reliability.Received(1).SearchIncidentsByOperatorAsync(
            operatorId,
            null,
            null,
            null,
            Arg.Any<IReadOnlyCollection<Guid>>(),
            null,
            null,
            null,
            ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL,
            null,
            null,
            Arg.Any<DateTimeOffset>(),
            1,
            20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidApprovalStatus_ReturnsValidationError()
    {
        var action = () => CreateHandler(Substitute.For<IParcelReliabilityRepository>()).Handle(
            new ListParcelIncidentsQuery(
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                null,
                null,
                "WAITING",
                null,
                null,
                1,
                20),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    private static ListParcelIncidentsQueryHandler CreateHandler(IParcelReliabilityRepository reliability)
        => new(
            reliability,
            Substitute.For<IParcelRepository>(),
            Substitute.For<IParcelCustodyExceptionRequestRepository>(),
            Substitute.For<ITripServiceClient>(),
            Substitute.For<IIdentityServiceClient>(),
            Substitute.For<IClock>());
}
