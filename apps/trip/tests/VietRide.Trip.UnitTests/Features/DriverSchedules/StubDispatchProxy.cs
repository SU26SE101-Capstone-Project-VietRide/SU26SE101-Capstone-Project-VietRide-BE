using System.Reflection;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

internal class StubDispatchProxy<T> : DispatchProxy
    where T : class
{
    private readonly Dictionary<string, int> callCounts = [];
    private readonly Dictionary<string, object?> results = [];
    private readonly Dictionary<string, object?[]?> lastArguments = [];

    public T Object => (T)(object)this;

    public static StubDispatchProxy<T> Create()
    {
        return (StubDispatchProxy<T>)(object)DispatchProxy.Create<T, StubDispatchProxy<T>>();
    }

    public void SetResult(string methodName, object? result)
    {
        results[methodName] = result;
    }

    public int CallCount(string methodName)
    {
        return callCounts.GetValueOrDefault(methodName);
    }

    public object?[]? LastArguments(string methodName)
    {
        return lastArguments.GetValueOrDefault(methodName);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        callCounts[targetMethod.Name] = CallCount(targetMethod.Name) + 1;
        lastArguments[targetMethod.Name] = args;
        results.TryGetValue(targetMethod.Name, out var configuredResult);

        if (targetMethod.ReturnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (targetMethod.ReturnType.IsGenericType
            && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
            var value = ConvertResult(resultType, configuredResult, args);
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [value]);
        }

        return configuredResult ?? GetDefault(targetMethod.ReturnType, args);
    }

    private static object? ConvertResult(Type resultType, object? configuredResult, object?[]? args)
    {
        if (configuredResult is not null && resultType.IsInstanceOfType(configuredResult))
        {
            return configuredResult;
        }

        if (configuredResult is true)
        {
            var constructor = resultType.GetConstructors().OrderBy(ctor => ctor.GetParameters().Length).FirstOrDefault();
            if (constructor is not null)
            {
                var constructorArguments = constructor.GetParameters()
                    .Select(parameter => parameter.ParameterType == typeof(bool)
                        ? (object)true
                        : GetDefault(parameter.ParameterType, null))
                    .ToArray();
                return constructor.Invoke(constructorArguments);
            }
        }

        return GetDefault(resultType, args);
    }

    private static object? GetDefault(Type type, object?[]? args)
    {
        if (args is { Length: > 0 } && type.IsInstanceOfType(args[0]))
        {
            return args[0];
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
