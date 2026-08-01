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
}
