using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Parcel.Api.Controllers;
using VietRide.Parcel.Application.Features.Parcels.TrackingAuthorization;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class InternalTripTrackingControllerTests
{
    [Fact]
    public async Task GetTrackingAuthorizationAsync_SendsParcelAuthorizationQuery()
    {
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetParcelTrackingAuthorizationQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ParcelTrackingAuthorizationResponse(true, "PARCEL_SENDER"));
        var controller = new InternalTripTrackingController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var response = await controller.GetTrackingAuthorizationAsync(
            tripId,
            userId,
            "PASSENGER",
            operatorId,
            CancellationToken.None);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<ParcelTrackingAuthorizationResponse>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().BeEquivalentTo(new ParcelTrackingAuthorizationResponse(true, "PARCEL_SENDER"));
        await mediator.Received(1).Send(
            Arg.Is<GetParcelTrackingAuthorizationQuery>(query =>
                query.TripId == tripId
                && query.UserId == userId
                && query.Role == "PASSENGER"
                && query.OperatorId == operatorId),
            Arg.Any<CancellationToken>());
    }
}
