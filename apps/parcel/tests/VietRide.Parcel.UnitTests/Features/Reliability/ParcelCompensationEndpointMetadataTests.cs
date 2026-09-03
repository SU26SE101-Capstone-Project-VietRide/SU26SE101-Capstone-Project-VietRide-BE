using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers;
using VietRide.Parcel.Api.Filters;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ParcelCompensationEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(OperatorParcelIncidentsController.PreviewClaimAwardAsync),
        "~/v1/operator/claims/{claimId:guid}/award-preview")]
    [InlineData(nameof(OperatorParcelIncidentsController.PreviewClaimAppealAdjustmentAsync),
        "~/v1/operator/claim-appeals/{appealId:guid}/adjustment-preview")]
    public void PreviewEndpoints_AreAdminOnlyReadOnlyPosts(string methodName, string route)
    {
        var method = typeof(OperatorParcelIncidentsController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");

        method.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be(route);
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttribute<SkipIdempotencyAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<RequireIdempotencyKeyAttribute>().Should().BeNull();
        ResponseStatuses(method).Should().BeEquivalentTo([200, 403, 404, 409, 422]);
    }

    [Theory]
    [InlineData(nameof(OperatorParcelIncidentsController.DecideClaimAsync))]
    [InlineData(nameof(OperatorParcelIncidentsController.DecideClaimAppealAsync))]
    public void DecisionEndpoints_RemainAdminOnlyAndIdempotent(string methodName)
    {
        var method = typeof(OperatorParcelIncidentsController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");

        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttribute<RequireIdempotencyKeyAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<SkipIdempotencyAttribute>().Should().BeNull();
        ResponseStatuses(method).Should().Contain(422);
    }

    private static IReadOnlyCollection<int> ResponseStatuses(MethodInfo method)
        => method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToArray();
}
