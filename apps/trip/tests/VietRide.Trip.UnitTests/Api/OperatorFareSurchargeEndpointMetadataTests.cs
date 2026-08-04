using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Filters;

namespace VietRide.Trip.UnitTests.Api;

public sealed class OperatorFareSurchargeEndpointMetadataTests
{
    [Fact]
    public void ReadEndpoints_AllowOperatorStaff()
    {
        var controller = typeof(OperatorFareSurchargesController);

        controller.GetMethod(nameof(OperatorFareSurchargesController.GetSettings))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles
            .Should().Be("OPERATOR_STAFF,OPERATOR_ADMIN");
        controller.GetMethod(nameof(OperatorFareSurchargesController.ListPeriods))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles
            .Should().Be("OPERATOR_STAFF,OPERATOR_ADMIN");
    }

    [Theory]
    [InlineData(nameof(OperatorFareSurchargesController.PutSettings))]
    [InlineData(nameof(OperatorFareSurchargesController.CreatePeriod))]
    [InlineData(nameof(OperatorFareSurchargesController.UpdatePeriod))]
    [InlineData(nameof(OperatorFareSurchargesController.DeletePeriod))]
    public void Mutation_IsOperatorAdminOnlyAndRequiresReplayIdempotency(string methodName)
    {
        var method = typeof(OperatorFareSurchargesController).GetMethod(methodName)!;

        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttribute<RequireIdempotencyKeyAttribute>().Should().NotBeNull();
    }
}
