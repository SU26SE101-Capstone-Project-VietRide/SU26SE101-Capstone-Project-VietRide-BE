using System.Reflection;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

internal class TestProxy<T> : DispatchProxy
    where T : class
{
    private Func<MethodInfo, object?[]?, object?> handler = (_, _) => null;

    public static T Create(Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create<T, TestProxy<T>>();
        ((TestProxy<T>)(object)proxy).handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var result = handler(targetMethod, args);
        var returnType = targetMethod.ReturnType;

        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return result as Task ?? Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            if (result is not null && returnType.IsInstanceOfType(result))
            {
                return result;
            }

            var resultType = returnType.GetGenericArguments()[0];
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result]);
        }

        return result;
    }
}
