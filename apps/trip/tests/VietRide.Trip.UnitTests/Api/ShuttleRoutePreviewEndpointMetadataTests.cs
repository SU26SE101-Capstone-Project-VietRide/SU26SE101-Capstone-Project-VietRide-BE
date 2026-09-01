using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.UnitTests.Api;

public sealed class ShuttleRoutePreviewEndpointMetadataTests
{
    [Fact]
    public void PreviewRoute_IsAdminOnlyReadOnlyPostWithApiEnvelope()
    {
        var method = typeof(OperatorShuttleController)
            .GetMethod(nameof(OperatorShuttleController.PreviewRoute))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template
            .Should().Be("shuttle-trips/route-preview");
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttribute<SkipIdempotencyAttribute>().Should().NotBeNull();
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Should().Contain(attribute =>
                attribute.StatusCode == StatusCodes.Status200OK
                && attribute.Type == typeof(ApiResponse<ShuttleRoutePreviewResult>));
    }
}
