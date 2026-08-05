using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;

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
}
