using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Trip.IntegrationTests;

public sealed class SharedIdempotencyMiddlewareIntegrationTests : IAsyncLifetime
{
    private readonly string _prefix = $"trip:test:shared-idem:{Guid.NewGuid():N}";
    private readonly HashSet<string> _keys = [];
    private IConnectionMultiplexer _redis = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("VIETRIDE_TEST_REDIS")
            ?? "localhost:6379,abortConnect=false,connectTimeout=3000";
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await _redis.GetDatabase().PingAsync();
    }

    public async Task DisposeAsync()
    {
        if (_redis is null)
        {
            return;
        }

        var redisKeys = _keys.SelectMany(key => new[]
        {
            LegacyKey(key),
            ResponseKey(key),
            ProcessingKey(key),
        }).ToArray();
        if (redisKeys.Length > 0)
        {
            await _redis.GetDatabase().KeyDeleteAsync(redisKeys);
        }

        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task MissingRequiredHeader_ReturnsExactRequiredError()
    {
        var invoked = 0;
        var result = await InvokeAsync(
            null,
            _ =>
            {
                invoked++;
                return Task.CompletedTask;
            });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Equal(IdempotencyMiddleware.RequiredErrorCode, ReadErrorCode(result.Body));
        Assert.Equal(0, invoked);
    }

    [Theory]
    [InlineData("", IdempotencyMiddleware.RequiredErrorCode)]
    [InlineData("not-a-uuid", "VALIDATION_ERROR")]
    [InlineData("00000000-0000-1000-8000-000000000000", "VALIDATION_ERROR")]
    public async Task MalformedRequiredHeader_ReturnsExactError(string key, string expectedErrorCode)
    {
        var invoked = 0;
        var result = await InvokeAsync(
            key,
            _ =>
            {
                invoked++;
                return Task.CompletedTask;
            });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Equal(expectedErrorCode, ReadErrorCode(result.Body));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task FirstRequestAndReplay_PreserveResponseAndV2Ttl()
    {
        var key = NewKey();
        var invoked = 0;
        RequestDelegate next = async context =>
        {
            invoked++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/vnd.vietride+json; charset=utf-8";
            await context.Response.WriteAsync("{\"result\":\"created\"}");
        };

        var first = await InvokeAsync(key, next);
        var replay = await InvokeAsync(key, next);

        Assert.Equal(1, invoked);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.BodyBytes, replay.BodyBytes);
        Assert.Equal(first.ContentType, replay.ContentType);
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(ProcessingKey(key)));
        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(ResponseKey(key));
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.FromHours(23), TimeSpan.FromHours(24));
    }

    [Theory]
    [InlineData("PATCH", "/v1/items/1", "", "{\"value\":1}", "user-a")]
    [InlineData("POST", "/v1/items/2", "", "{\"value\":1}", "user-a")]
    [InlineData("POST", "/v1/items/1", "?a=1", "{\"value\":1}", "user-a")]
    [InlineData("POST", "/v1/items/1", "", "{ \"value\": 1 }", "user-a")]
    [InlineData("POST", "/v1/items/1", "", "{\"value\":1}", "user-b")]
    public async Task SameKeyChangedFingerprint_ReturnsMismatch(
        string method,
        string path,
        string query,
        string body,
        string subject)
    {
        var key = NewKey();
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("original");
        };

        await InvokeAsync(key, next);
        var mismatch = await InvokeAsync(
            key,
            next,
            RequestShape.Default with
            {
                Method = method,
                Path = path,
                Query = query,
                Body = body,
                Subject = subject,
            });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal(IdempotencyMiddleware.MismatchErrorCode, ReadErrorCode(mismatch.Body));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task CanonicalQueryOrderAndDuplicateValues_ReplaySameResponse()
    {
        var key = NewKey();
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("canonical-query");
        };

        var first = await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Query = "?b=2&a=3&a=1" });
        var replay = await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Query = "?a=1&b=2&a=3" });

        Assert.Equal(1, invoked);
        Assert.Equal(first.BodyBytes, replay.BodyBytes);
    }

    [Fact]
    public async Task EmptyBodyDifferentPath_ReturnsMismatch()
    {
        var key = NewKey();
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("arrived");
        };

        await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Path = "/v1/trips/1/stops/1/arrive", Body = string.Empty });
        var mismatch = await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Path = "/v1/trips/2/stops/1/arrive", Body = string.Empty });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal(IdempotencyMiddleware.MismatchErrorCode, ReadErrorCode(mismatch.Body));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task ConcurrentSameFingerprint_HasOneExecutionAndPendingLoser()
    {
        var key = NewKey();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoked = 0;
        RequestDelegate next = async context =>
        {
            Interlocked.Increment(ref invoked);
            entered.SetResult();
            await release.Task;
            await context.Response.WriteAsync("done");
        };

        var winnerTask = InvokeAsync(key, next);
        await entered.Task;
        var processingTtl = await _redis.GetDatabase().KeyTimeToLiveAsync(ProcessingKey(key));
        var loser = await InvokeAsync(key, next);
        release.SetResult();
        var winner = await winnerTask;

        Assert.Equal("done", winner.Body);
        Assert.Equal(StatusCodes.Status409Conflict, loser.StatusCode);
        Assert.Equal(IdempotencyMiddleware.PendingErrorCode, ReadErrorCode(loser.Body));
        Assert.Equal(1, invoked);
        Assert.NotNull(processingTtl);
        Assert.InRange(processingTtl!.Value, TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task FiveHundredAndException_ReleaseLockWithoutCaching()
    {
        var responseKey = NewKey();
        var invocations = 0;
        RequestDelegate failThenSucceed = async context =>
        {
            invocations++;
            if (invocations == 1)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("temporary");
                return;
            }

            await context.Response.WriteAsync("recovered");
        };

        var failure = await InvokeAsync(responseKey, failThenSucceed);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, failure.StatusCode);
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(ProcessingKey(responseKey)));
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(ResponseKey(responseKey)));
        var retry = await InvokeAsync(responseKey, failThenSucceed);
        Assert.Equal("recovered", retry.Body);
        Assert.Equal(2, invocations);

        var exceptionKey = NewKey();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(
            exceptionKey,
            _ => throw new InvalidOperationException("controlled")));
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(ProcessingKey(exceptionKey)));
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(ResponseKey(exceptionKey)));
    }

    [Fact]
    public async Task LegacyEntry_FailsClosedForOptedInAndUnannotatedMutation()
    {
        var key = NewKey();
        await _redis.GetDatabase().StringSetAsync(
            LegacyKey(key),
            "{\"statusCode\":200,\"body\":\"bGVnYWN5\",\"bodyHash\":\"ABC\"}",
            TimeSpan.FromHours(24));
        var invoked = 0;
        RequestDelegate next = _ =>
        {
            invoked++;
            return Task.CompletedTask;
        };

        var optedIn = await InvokeAsync(key, next);
        var unannotated = await InvokeAsync(key, next, optedIn: false);

        Assert.Equal(IdempotencyMiddleware.MismatchErrorCode, ReadErrorCode(optedIn.Body));
        Assert.Equal(IdempotencyMiddleware.MismatchErrorCode, ReadErrorCode(unannotated.Body));
        Assert.Equal(0, invoked);
        Assert.True(await _redis.GetDatabase().KeyExistsAsync(LegacyKey(key)));
    }

    [Fact]
    public async Task ExpiredStaleOwner_CannotDeleteOrOverwriteNewOwner()
    {
        var key = NewKey();
        var staleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleTask = InvokeAsync(
            key,
            async context =>
            {
                staleEntered.SetResult();
                await releaseStale.Task;
                await context.Response.WriteAsync("stale");
            });
        await staleEntered.Task;
        var stalePayload = (await _redis.GetDatabase().StringGetAsync(ProcessingKey(key))).ToString();
        await ExpireAsync(ProcessingKey(key));

        var newerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNewer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerTask = InvokeAsync(
            key,
            async context =>
            {
                newerEntered.SetResult();
                await releaseNewer.Task;
                await context.Response.WriteAsync("newer");
            });
        await newerEntered.Task;
        var newerPayload = (await _redis.GetDatabase().StringGetAsync(ProcessingKey(key))).ToString();
        Assert.NotEqual(ReadOwnerToken(stalePayload), ReadOwnerToken(newerPayload));

        releaseStale.SetResult();
        Assert.Equal("stale", (await staleTask).Body);
        Assert.Equal(newerPayload, (await _redis.GetDatabase().StringGetAsync(ProcessingKey(key))).ToString());
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(ResponseKey(key)));

        releaseNewer.SetResult();
        Assert.Equal("newer", (await newerTask).Body);
        var replay = await InvokeAsync(key, _ => throw new InvalidOperationException("must not execute"));
        Assert.Equal("newer", replay.Body);
    }

    private async Task ExpireAsync(RedisKey key)
    {
        Assert.True(await _redis.GetDatabase().KeyExpireAsync(key, TimeSpan.FromMilliseconds(1)));
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (!await _redis.GetDatabase().KeyExistsAsync(key))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The controlled processing lock did not expire.");
    }

    private async Task<ResponseSnapshot> InvokeAsync(
        string? key,
        RequestDelegate next,
        RequestShape? shape = null,
        bool optedIn = true,
        bool allowRequestBody = true)
    {
        shape ??= RequestShape.Default;
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = shape.Method;
        context.Request.PathBase = shape.PathBase;
        context.Request.Path = shape.Path;
        context.Request.QueryString = new QueryString(shape.Query);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(shape.Body));
        context.Request.ContentType = "application/json";
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(shape.SubjectClaimType, shape.Subject)], "test"));
        if (optedIn)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new RequireIdempotencyAttribute
                {
                    AllowRequestBody = allowRequestBody,
                }),
                "test"));
        }
        else
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(),
                "test"));
        }

        if (key is not null)
        {
            context.Request.Headers[IdempotencyMiddleware.IdempotencyKeyHeader] = key;
        }

        var middleware = new IdempotencyMiddleware(
            next,
            _redis,
            new IdempotencyOptions { ServicePrefix = _prefix },
            NullLogger<IdempotencyMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        return new ResponseSnapshot(context.Response.StatusCode, context.Response.ContentType, bytes);
    }

    private string NewKey()
    {
        var key = Guid.NewGuid().ToString("D");
        _keys.Add(key);
        return key;
    }

    private RedisKey LegacyKey(string key) => $"{_prefix}:idem:{key}";

    private RedisKey ResponseKey(string key)
        => $"{_prefix}:idem:v2:response:{HashKey(key)}";

    private RedisKey ProcessingKey(string key)
        => $"{_prefix}:idem:v2:processing:{HashKey(key)}";

    private static string HashKey(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string ReadErrorCode(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    private static string ReadOwnerToken(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("ownerToken").GetString()!;
    }

    private sealed record RequestShape(
        string Method,
        string PathBase,
        string Path,
        string Query,
        string Subject,
        string Body)
    {
        public string SubjectClaimType { get; init; } = "sub";

        public static RequestShape Default { get; } = new(
            HttpMethods.Post,
            string.Empty,
            "/v1/items/1",
            string.Empty,
            "user-a",
            "{\"value\":1}");
    }

    private sealed record ResponseSnapshot(int StatusCode, string? ContentType, byte[] BodyBytes)
    {
        public string Body => Encoding.UTF8.GetString(BodyBytes);
    }
}
