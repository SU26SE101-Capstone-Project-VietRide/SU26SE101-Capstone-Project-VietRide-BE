using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;

namespace VietRide.Trip.UnitTests.Api;

public sealed class DisruptNoSubstitutionEndpointMetadataTests
{
    [Fact]
    public void Endpoint_IsOperatorAdminOnlyAndUsesSharedReplayIdempotency()
    {
        var method = typeof(OperatorTripsController)
            .GetMethod(nameof(OperatorTripsController.DisruptNoSubstitutionAsync))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template
            .Should().Be("{tripId:guid}/disrupt-no-substitution");
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles
            .Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttribute<RequireIdempotencyAttribute>()
            .Should().NotBeNull();
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().Contain([200, 403, 404, 409, 422]);
    }
}
