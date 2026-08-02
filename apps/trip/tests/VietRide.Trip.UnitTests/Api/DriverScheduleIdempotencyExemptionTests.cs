using System.Reflection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;

namespace VietRide.Trip.UnitTests.Api;

public sealed class DriverScheduleIdempotencyExemptionTests
{
    [Fact]
    public void MutationMetadata_ExemptsOnlyCreateAndActivate()
    {
        Assert.NotNull(GetSkipMetadata(nameof(OperatorDriverSchedulesController.Create)));
        Assert.NotNull(GetSkipMetadata(nameof(OperatorDriverSchedulesController.Activate)));

        Assert.Null(GetSkipMetadata(nameof(OperatorDriverSchedulesController.Update)));
        Assert.Null(GetSkipMetadata(nameof(OperatorDriverSchedulesController.UpdateCrew)));
    }

    private static SkipIdempotencyAttribute? GetSkipMetadata(string name) =>
        GetMethod(name).GetCustomAttribute<SkipIdempotencyAttribute>();

    private static MethodInfo GetMethod(string name) =>
        typeof(OperatorDriverSchedulesController).GetMethod(name)
        ?? throw new InvalidOperationException($"Controller action {name} was not found.");
}
