using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using VietRide.Shared.Web.ModelBinding;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.ModelBinding;

public sealed class UtcDateTimeOffsetModelBinderTests
{
    [Fact]
    public async Task BindModelAsync_NormalizesExplicitQueryOffsetToUtc()
    {
        var context = CreateContext("2026-08-10T17:00:00+07:00");

        await new UtcDateTimeOffsetModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        context.Result.Model.Should().Be(
            new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero));
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task BindModelAsync_RejectsQueryTimestampWithoutOffset()
    {
        var context = CreateContext("2026-08-10T17:00:00");

        await new UtcDateTimeOffsetModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-08-10 17:00:00+07:00")]
    [InlineData("2026-02-30T17:00:00Z")]
    public async Task BindModelAsync_RejectsNonRfc3339Timestamp(string value)
    {
        var context = CreateContext(value);

        await new UtcDateTimeOffsetModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    private static ModelBindingContext CreateContext(string value)
    {
        var metadataProvider = new EmptyModelMetadataProvider();
        var modelState = new ModelStateDictionary();
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            modelState);
        var values = new Dictionary<string, StringValues> { ["from"] = value };
        var valueProvider = new QueryStringValueProvider(
            BindingSource.Query,
            new QueryCollection(values),
            CultureInfo.InvariantCulture);

        return DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadataProvider.GetMetadataForType(typeof(DateTimeOffset?)),
            bindingInfo: null,
            modelName: "from");
    }
}
