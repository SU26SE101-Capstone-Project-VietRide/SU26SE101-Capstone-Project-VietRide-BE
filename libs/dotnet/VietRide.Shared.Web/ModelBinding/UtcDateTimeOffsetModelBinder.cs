using Microsoft.AspNetCore.Mvc.ModelBinding;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Shared.Web.ModelBinding;

/// <summary>
/// Applies the explicit-offset UTC contract to DateTimeOffset values outside JSON bodies
/// (query string, route values and form values).
/// </summary>
public sealed class UtcDateTimeOffsetModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);
        var raw = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) is not null)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
                return Task.CompletedTask;
            }

            AddError(bindingContext);
            return Task.CompletedTask;
        }

        if (!UtcJson.TryParseInstant(raw, out var parsed))
        {
            AddError(bindingContext);
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(parsed.ToUniversalTime());
        return Task.CompletedTask;
    }

    private static void AddError(ModelBindingContext context) =>
        context.ModelState.TryAddModelError(
            context.ModelName,
            "Timestamp must be a valid ISO-8601 value with Z or an explicit offset.");
}

public sealed class UtcDateTimeOffsetModelBinderProvider : IModelBinderProvider
{
    private static readonly IModelBinder Binder = new UtcDateTimeOffsetModelBinder();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type == typeof(DateTimeOffset) ? Binder : null;
    }
}
