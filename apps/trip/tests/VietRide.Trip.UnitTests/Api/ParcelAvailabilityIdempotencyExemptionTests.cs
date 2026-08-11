using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;

namespace VietRide.Trip.UnitTests.Api;

public sealed class ParcelAvailabilityIdempotencyExemptionTests
{
    [Fact]
    public void SearchPost_IsExplicitlyExemptBecauseItIsReadOnly()
    {
        var action = typeof(InternalTripsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.GetCustomAttribute<HttpPostAttribute>()?.Template == "parcel-availability/search");

        var exemption = action.GetCustomAttribute<SkipIdempotencyAttribute>();

        Assert.NotNull(exemption);
        Assert.False(string.IsNullOrWhiteSpace(exemption.Reason));
    }
}
