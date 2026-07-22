using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Parcel.Api.Controllers;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Application.Features.Parcels.MarkLoaded;

namespace VietRide.Parcel.UnitTests.Features.Parcels.MarkLoaded;

public sealed class Day29AssistantLoadCommandTests
{
    [Fact]
    public async Task MapsAuthenticatedAssistantAndStableCargoMutationInputs()
    {
        var parcelId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var assistantUserId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        const string parcelCode = "VRP-20260722-ABCDEFGH";
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<MarkParcelLoadedCommand>(), Arg.Any<CancellationToken>())
            .Returns(new MarkParcelLoadedResponse(parcelId, parcelCode, "LOADED"));
        var controller = new AssistantParcelsController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("sub", assistantUserId.ToString("D")),
                        new Claim("role", "ASSISTANT"),
                        new Claim("operatorId", operatorId.ToString("D")),
                    ], "test")),
                },
            },
        };

        var result = await controller.LoadAsync(
            parcelId,
            new LoadParcelRequest(tripId, parcelCode),
            CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        await mediator.Received(1).Send(
            Arg.Is<MarkParcelLoadedCommand>(command =>
                command.ParcelId == parcelId
                && command.TripId == tripId
                && command.ParcelCode == parcelCode
                && command.LoadedByUserId == assistantUserId
                && command.OperatorId == operatorId),
            Arg.Any<CancellationToken>());
    }
}
