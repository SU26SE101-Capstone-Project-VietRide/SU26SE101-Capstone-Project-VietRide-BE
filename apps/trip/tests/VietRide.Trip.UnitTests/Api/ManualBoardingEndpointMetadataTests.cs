using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;

namespace VietRide.Trip.UnitTests.Api;

public sealed class ManualBoardingEndpointMetadataTests
{
    [Fact]
    public void DriverEndpoint_IsExactDriverOnlyBodylessMutation()
    {
        AssertEndpoint(
            typeof(DriverController).GetMethod(nameof(DriverController.StartBoardingAsync))!,
            "trips/{tripId}/boarding",
            "DRIVER");
    }

    [Fact]
    public void OperatorEndpoint_IsExactOperatorAdminOnlyBodylessMutation()
    {
        AssertEndpoint(
            typeof(OperatorTripsController).GetMethod(nameof(OperatorTripsController.StartBoardingAsync))!,
            "{tripId}/boarding",
            "OPERATOR_ADMIN");
    }

    private static void AssertEndpoint(MethodInfo method, string route, string role)
    {
        method.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be(route);
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(role);
        method.GetCustomAttribute<RequireIdempotencyAttribute>()!.AllowRequestBody.Should().BeFalse();
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().Contain([200, 401, 403, 404, 409, 422]);
    }
}
