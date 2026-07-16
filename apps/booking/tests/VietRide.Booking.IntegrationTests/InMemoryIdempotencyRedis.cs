using System.Text.Json;
using NSubstitute;
using StackExchange.Redis;

namespace VietRide.Booking.IntegrationTests;

internal static class InMemoryIdempotencyRedis
{
    public static IConnectionMultiplexer Create()
    {
        var store = new Dictionary<string, RedisValue>(StringComparer.Ordinal);
        var database = Substitute.For<IDatabase>();
        database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(call => store.ContainsKey(Key(call.ArgAt<RedisKey>(0))));
        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(call => store.TryGetValue(Key(call.ArgAt<RedisKey>(0)), out var value)
                ? value
                : RedisValue.Null);
        database.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<When>(),
                Arg.Any<CommandFlags>())
            .Returns(call => Set(
                store,
                Key(call.ArgAt<RedisKey>(0)),
                call.ArgAt<RedisValue>(1),
                call.ArgAt<When>(3)));
        database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(call => store.Remove(Key(call.ArgAt<RedisKey>(0))));
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(call => Evaluate(
                store,
                call.ArgAt<RedisKey[]>(1),
                call.ArgAt<RedisValue[]>(2)));

        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        return multiplexer;
    }

    private static bool Set(
        IDictionary<string, RedisValue> store,
        string key,
        RedisValue value,
        When when)
    {
        if (when == When.NotExists && store.ContainsKey(key))
        {
            return false;
        }

        store[key] = value;
        return true;
    }

    private static RedisResult Evaluate(
        IDictionary<string, RedisValue> store,
        IReadOnlyList<RedisKey> keys,
        IReadOnlyList<RedisValue> values)
    {
        if (keys.Count == 0 || values.Count == 0)
        {
            return Result(0);
        }

        var processingKey = Key(keys[0]);
        if (!store.TryGetValue(processingKey, out var current))
        {
            return Result(0);
        }

        using var document = JsonDocument.Parse(current.ToString());
        var root = document.RootElement;
        var currentOwnerToken = root.GetProperty("ownerToken").GetString();
        if (values.Count == 1)
        {
            if (!string.Equals(currentOwnerToken, values[0].ToString(), StringComparison.Ordinal))
            {
                return Result(0);
            }

            store.Remove(processingKey);
            return Result(1);
        }

        if (keys.Count < 2 || values.Count < 3)
        {
            return Result(0);
        }

        var currentFingerprint = root.GetProperty("requestFingerprint").GetString();
        if (!string.Equals(currentFingerprint, values[0].ToString(), StringComparison.Ordinal)
            || !string.Equals(currentOwnerToken, values[1].ToString(), StringComparison.Ordinal))
        {
            return Result(0);
        }

        store[Key(keys[1])] = values[2];
        store.Remove(processingKey);
        return Result(1);
    }

    private static RedisResult Result(long value)
        => RedisResult.Create((RedisValue)value);

    private static string Key(RedisKey key) => key.ToString();
}
