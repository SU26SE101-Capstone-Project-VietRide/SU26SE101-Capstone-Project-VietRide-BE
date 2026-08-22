using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Shuttle;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Api;

public sealed class ShuttlePickupEndpointMetadataTests
{
    [Fact]
    public void Endpoint_IsDriverOnlyBodylessAndIdempotent()
    {
        var method = typeof(DriverController)
            .GetMethod(nameof(DriverController.MarkShuttlePickupAsync))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template
            .Should().Be("shuttle-trips/{shuttleTripId:guid}/stops/{pickupOrder:int}/pickup");
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("DRIVER");
        method.GetCustomAttribute<RequireIdempotencyAttribute>()!.AllowRequestBody.Should().BeFalse();
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo([200, 401, 403, 404, 409, 422]);
    }

    [Theory]
    [InlineData(nameof(DriverController.GetMyShuttleTripsAsync), "shuttle-trips", 200, 401, 403, 422)]
    [InlineData(
        nameof(DriverController.GetMyShuttleManifestAsync),
        "shuttle-trips/{shuttleTripId:guid}/manifest",
        200,
        401,
        403,
        404)]
    public void ShuttleReadEndpoints_AreDriverOnlyAndExposeDocumentedResponses(
        string methodName,
        string route,
        params int[] responseCodes)
    {
        var method = typeof(DriverController).GetMethod(methodName)!;

        method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be(route);
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("DRIVER");
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo(responseCodes);
    }

    [Fact]
    public void OperatorPassengerContacts_IsTenantAuthorizedGetWithDocumentedResponses()
    {
        var method = typeof(OperatorShuttleController)
            .GetMethod(nameof(OperatorShuttleController.GetPassengerContacts))!;

        method.GetCustomAttribute<HttpGetAttribute>()!.Template
            .Should().Be("shuttle-trips/{shuttleTripId:guid}/passengers");
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles
            .Should().Be("OPERATOR_ADMIN,OPERATOR_STAFF");
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo([200, 403, 404, 503]);
    }

    [Fact]
    public async Task OperatorPassengerContacts_DispatchesTenantQueryAndDisablesCaching()
    {
        var operatorId = Guid.NewGuid();
        var shuttleTripId = Guid.NewGuid();
        var response = new ShuttlePassengerContactResponse(shuttleTripId, []);
        var sender = TestProxy<ISender>.Create((method, args) =>
        {
            if (method.Name == nameof(ISender.Send))
            {
                var query = Assert.IsType<GetShuttlePassengerContactsQuery>(args![0]);
                query.OperatorId.Should().Be(operatorId);
                query.ShuttleTripId.Should().Be(shuttleTripId);
                return response;
            }

            return null;
        });
        var controller = new OperatorShuttleController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("operatorId", operatorId.ToString())],
                        "test")),
                },
            },
        };

        var result = await controller.GetPassengerContacts(shuttleTripId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(response);
        controller.Response.Headers.CacheControl.ToString().Should().Be("private, no-store");
    }

    [Fact]
    public void OperatorShuttleReassignment_IsAdminOnlyPatchAndRequiresIdempotencyKey()
    {
        var method = typeof(OperatorShuttleController)
            .GetMethod(nameof(OperatorShuttleController.ReassignTrip))!;

        method.GetCustomAttribute<HttpPatchAttribute>()!.Template
            .Should().Be("shuttle-trips/{shuttleTripId:guid}/assignment");
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttribute<RequireIdempotencyKeyAttribute>().Should().NotBeNull();
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo([200, 403, 404, 409, 422, 503]);
    }
}
