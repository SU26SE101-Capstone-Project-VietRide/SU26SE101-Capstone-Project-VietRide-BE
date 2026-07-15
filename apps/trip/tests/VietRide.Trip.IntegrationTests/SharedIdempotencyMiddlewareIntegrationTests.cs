using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Trip.IntegrationTests;

public sealed class SharedIdempotencyMiddlewareIntegrationTests : IAsyncLifetime
{
    private readonly string _prefix = $"trip:test:shared-idem:{Guid.NewGuid():N}";
    private readonly ConcurrentBag<string> _keys = new();
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

        var database = _redis.GetDatabase();
        await database.KeyDeleteAsync(_keys.Select(key => (RedisKey)$"{_prefix}:idem:{key}").ToArray());
        await _redis.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-1000-8000-000000000000")]
    public async Task OptedInEndpoint_MissingOrMalformedKey_ReturnsValidationError(string? key)
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
        Assert.Equal("VALIDATION_ERROR", ReadErrorCode(result.Body));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task OptedInEndpoint_OutsideLegacyMethodGate_MissingKey_ReturnsValidationError()
    {
        var invoked = 0;
        var result = await InvokeAsync(
            null,
            _ =>
            {
                invoked++;
                return Task.CompletedTask;
            },
            RequestShape.Default with { Method = HttpMethods.Delete });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ReadErrorCode(result.Body));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task NoBodyEndpoint_NonEmptyBodyIsRejectedBeforeReservationAndSameKeyCanExecuteEmptyBody()
    {
        var key = NewKey();
        var redisKey = (RedisKey)$"{_prefix}:idem:{key}";
        var invoked = 0;
        RequestDelegate next = async context =>
        {
            invoked++;
            Assert.Equal(0, context.Request.Body.Position);
            await context.Response.WriteAsync("executed");
        };

        var rejected = await InvokeAsync(
            key,
            next,
            RequestShape.Default,
            allowRequestBody: false);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, rejected.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ReadErrorCode(rejected.Body));
        Assert.Equal(0, invoked);
        Assert.False(await _redis.GetDatabase().KeyExistsAsync(redisKey));

        var valid = await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Body = string.Empty },
            allowRequestBody: false);

        Assert.Equal(StatusCodes.Status200OK, valid.StatusCode);
        Assert.Equal("executed", valid.Body);
        Assert.Equal(1, invoked);
        Assert.True(await _redis.GetDatabase().KeyExistsAsync(redisKey));
    }

    [Fact]
    public async Task FirstRequest_ReservesExecutesAndCompletedRetryReplaysExactResponse()
    {
        var key = NewKey();
        var invoked = 0;
        RequestDelegate next = async context =>
        {
            invoked++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync("{\"result\":\"created\"}");
        };

        var first = await InvokeAsync(key, next);
        var replay = await InvokeAsync(key, next);

        Assert.Equal(1, invoked);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.BodyBytes, replay.BodyBytes);
    }

    [Fact]
    public async Task NameIdentifierSubject_SameSubject_ReplaysExactResponse()
    {
        var key = NewKey();
        var invoked = 0;
        var shape = RequestShape.Default with { SubjectClaimType = ClaimTypes.NameIdentifier };
        RequestDelegate next = async context =>
        {
            invoked++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync("{\"result\":\"created\"}");
        };

        var first = await InvokeAsync(key, next, shape);
        var replay = await InvokeAsync(key, next, shape);

        Assert.Equal(1, invoked);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.BodyBytes, replay.BodyBytes);
    }

    [Fact]
    public async Task NameIdentifierSubject_DifferentSubjectWithSameRequest_ReturnsMismatchWithoutDownstreamExecution()
    {
        var key = NewKey();
        var invoked = 0;
        var firstShape = RequestShape.Default with { SubjectClaimType = ClaimTypes.NameIdentifier };
        var differentSubject = firstShape with { Subject = "user-b" };
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("original");
        };

        await InvokeAsync(key, next, firstShape);
        var mismatch = await InvokeAsync(key, next, differentSubject);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", ReadErrorCode(mismatch.Body));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task AuthenticatedPrincipal_WithBlankSubject_ReturnsUnauthorizedWithoutDownstreamExecution()
    {
        var invoked = 0;
        var result = await InvokeAsync(
            NewKey(),
            _ =>
            {
                invoked++;
                return Task.CompletedTask;
            },
            RequestShape.Default with
            {
                Subject = " ",
                SubjectClaimType = ClaimTypes.NameIdentifier,
            });

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal("UNAUTHORIZED", ReadErrorCode(result.Body));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task ConcurrentSameFingerprint_WhileReserved_ReturnsPending()
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

        var firstTask = InvokeAsync(key, next);
        await entered.Task;
        var concurrent = await InvokeAsync(key, next);
        release.SetResult();
        await firstTask;

        Assert.Equal(StatusCodes.Status409Conflict, concurrent.StatusCode);
        Assert.Equal("IDEMPOTENCY_REQUEST_PENDING", ReadErrorCode(concurrent.Body));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task SameKey_WithChangedFingerprintComponent_ReturnsMismatch()
    {
        var cases = new[]
        {
            new RequestShape(HttpMethods.Patch, "/v1/items/{id}", "11111111-1111-4111-8111-111111111111", "user-a", "{\"value\":1}"),
            new RequestShape(HttpMethods.Post, "/v1/other/{id}", "11111111-1111-4111-8111-111111111111", "user-a", "{\"value\":1}"),
            new RequestShape(HttpMethods.Post, "/v1/items/{id}", "22222222-2222-4222-8222-222222222222", "user-a", "{\"value\":1}"),
            new RequestShape(HttpMethods.Post, "/v1/items/{id}", "11111111-1111-4111-8111-111111111111", "user-b", "{\"value\":1}"),
            new RequestShape(HttpMethods.Post, "/v1/items/{id}", "11111111-1111-4111-8111-111111111111", "user-a", "{\"value\":2}"),
        };

        foreach (var changed in cases)
        {
            var key = NewKey();
            var invoked = 0;
            RequestDelegate next = context =>
            {
                invoked++;
                return context.Response.WriteAsync("original");
            };

            await InvokeAsync(key, next, RequestShape.Default);
            var mismatch = await InvokeAsync(key, next, changed);

            Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
            Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", ReadErrorCode(mismatch.Body));
            Assert.Equal(1, invoked);
        }
    }

    [Fact]
    public async Task SameKey_ReusedOutsideLegacyMethodGate_ReturnsMismatchWithoutDownstreamExecution()
    {
        var key = NewKey();
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("original");
        };

        await InvokeAsync(key, next, RequestShape.Default);
        var mismatch = await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Method = HttpMethods.Delete });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", ReadErrorCode(mismatch.Body));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task JsonPropertyOrder_IsCanonicalized()
    {
        var key = NewKey();
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("canonical");
        };

        var firstShape = RequestShape.Default with { Body = "{\"alpha\":1,\"nested\":{\"x\":2,\"y\":3}}" };
        var reorderedShape = RequestShape.Default with { Body = "{ \"nested\": { \"y\": 3, \"x\": 2 }, \"alpha\": 1 }" };

        var first = await InvokeAsync(key, next, firstShape);
        var replay = await InvokeAsync(key, next, reorderedShape);

        Assert.Equal(1, invoked);
        Assert.Equal(first.BodyBytes, replay.BodyBytes);
    }

    [Fact]
    public async Task QueryKeys_AreCanonicalizedButValuesAndAbsenceRemainFingerprintComponents()
    {
        var replayKey = NewKey();
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("query-aware");
        };
        var firstShape = RequestShape.Default with { Query = "?applyTo=ALL_PENDING&z=1" };
        var reordered = RequestShape.Default with { Query = "?z=1&applyTo=ALL_PENDING" };

        var first = await InvokeAsync(replayKey, next, firstShape);
        var replay = await InvokeAsync(replayKey, next, reordered);

        Assert.Equal(first.BodyBytes, replay.BodyBytes);
        Assert.Equal(1, invoked);

        var mismatchKey = NewKey();
        var mismatch = await InvokeAsync(
            mismatchKey,
            next,
            firstShape);
        Assert.Equal(StatusCodes.Status200OK, mismatch.StatusCode);
        var changed = await InvokeAsync(
            mismatchKey,
            next,
            firstShape with { Query = "?applyTo=FUTURE_ONLY&z=1" });
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, changed.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", ReadErrorCode(changed.Body));
    }

    [Fact]
    public async Task UnannotatedEndpoint_WithoutKey_PreservesPassThroughBehavior()
    {
        var invoked = 0;
        var result = await InvokeAsync(
            null,
            context =>
            {
                invoked++;
                return context.Response.WriteAsync("legacy");
            },
            RequestShape.Default,
            optedIn: false);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("legacy", result.Body);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task UnannotatedEndpoint_WithLegacyKey_PreservesFirstReplayMismatchAndFiveHundredRetryBehavior()
    {
        const string legacyKey = "legacy-key-format";
        _keys.Add(legacyKey);
        var invoked = 0;
        RequestDelegate success = async context =>
        {
            invoked++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync("legacy-created");
        };

        var first = await InvokeAsync(legacyKey, success, optedIn: false);
        var replay = await InvokeAsync(legacyKey, success, optedIn: false);
        var mismatch = await InvokeAsync(
            legacyKey,
            success,
            RequestShape.Default with { Body = "{\"value\":2}" },
            optedIn: false);

        Assert.Equal(StatusCodes.Status201Created, first.StatusCode);
        Assert.Equal("legacy-created", first.Body);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.BodyBytes, replay.BodyBytes);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", ReadErrorCode(mismatch.Body));
        Assert.Equal(1, invoked);

        const string retryKey = "legacy-retry-key-format";
        _keys.Add(retryKey);
        var retryInvocations = 0;
        RequestDelegate failThenSucceed = async context =>
        {
            retryInvocations++;
            if (retryInvocations == 1)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("temporary-failure");
                return;
            }

            await context.Response.WriteAsync("recovered");
        };

        var failure = await InvokeAsync(retryKey, failThenSucceed, optedIn: false);
        var retry = await InvokeAsync(retryKey, failThenSucceed, optedIn: false);
        var retryReplay = await InvokeAsync(retryKey, failThenSucceed, optedIn: false);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, failure.StatusCode);
        Assert.Equal("temporary-failure", failure.Body);
        Assert.Equal(StatusCodes.Status200OK, retry.StatusCode);
        Assert.Equal("recovered", retry.Body);
        Assert.Equal(retry.BodyBytes, retryReplay.BodyBytes);
        Assert.Equal(2, retryInvocations);
    }

    [Fact]
    public async Task UnannotatedEndpoint_PreDeployEntry_ReplaysSameRawBodyAndRejectsChangedBody()
    {
        var key = NewKey();
        var originalRequestBody = "{\"value\":1}";
        var responseBytes = Encoding.UTF8.GetBytes("{\"result\":\"legacy-cached\"}");
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalRequestBody)));
        var exactLegacyJson =
            $"{{\"statusCode\":201,\"body\":\"{Convert.ToBase64String(responseBytes)}\",\"bodyHash\":\"{bodyHash}\"}}";
        var redisKey = $"{_prefix}:idem:{key}";
        await _redis.GetDatabase().StringSetAsync(redisKey, exactLegacyJson, TimeSpan.FromHours(24));

        var invoked = 0;
        RequestDelegate next = _ =>
        {
            invoked++;
            return Task.CompletedTask;
        };

        var replay = await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Body = originalRequestBody },
            optedIn: false);
        var mismatch = await InvokeAsync(
            key,
            next,
            RequestShape.Default with { Body = "{ \"value\": 1 }" },
            optedIn: false);

        Assert.Equal(StatusCodes.Status201Created, replay.StatusCode);
        Assert.Equal(responseBytes, replay.BodyBytes);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", ReadErrorCode(mismatch.Body));
        Assert.Equal(0, invoked);
        Assert.Equal(exactLegacyJson, await _redis.GetDatabase().StringGetAsync(redisKey));
    }

    [Fact]
    public async Task OptedInEndpoint_PreDeployEntry_DoesNotUseLegacyBodyOnlyReplay()
    {
        var key = NewKey();
        var requestBody = RequestShape.Default.Body;
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestBody)));
        var exactLegacyJson =
            $"{{\"statusCode\":200,\"body\":\"{Convert.ToBase64String(Encoding.UTF8.GetBytes("legacy"))}\",\"bodyHash\":\"{bodyHash}\"}}";
        await _redis.GetDatabase().StringSetAsync(
            $"{_prefix}:idem:{key}",
            exactLegacyJson,
            TimeSpan.FromHours(24));

        var invoked = 0;
        var mismatch = await InvokeAsync(
            key,
            _ =>
            {
                invoked++;
                return Task.CompletedTask;
            });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", ReadErrorCode(mismatch.Body));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task DownstreamException_ReleasesReservationForDeterministicRetry()
    {
        var key = NewKey();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(
            key,
            _ => throw new InvalidOperationException("controlled failure")));

        var invoked = 0;
        var retry = await InvokeAsync(
            key,
            context =>
            {
                invoked++;
                return context.Response.WriteAsync("recovered");
            });

        Assert.Equal(StatusCodes.Status200OK, retry.StatusCode);
        Assert.Equal("recovered", retry.Body);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task ExpiredStaleOwner_CleanupCannotDeleteNewerReservation()
    {
        var key = NewKey();
        var redisKey = (RedisKey)$"{_prefix}:idem:{key}";
        var staleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failStale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var staleTask = InvokeAsync(
            key,
            async _ =>
            {
                staleEntered.SetResult();
                await failStale.Task;
                throw new InvalidOperationException("stale request failed");
            });
        await staleEntered.Task;
        var stalePayload = (await _redis.GetDatabase().StringGetAsync(redisKey)).ToString();

        await ExpireAsync(redisKey);

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
        var newerPayload = (await _redis.GetDatabase().StringGetAsync(redisKey)).ToString();

        Assert.NotEqual(ReadReservationToken(stalePayload), ReadReservationToken(newerPayload));

        failStale.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => staleTask);

        Assert.Equal(newerPayload, (await _redis.GetDatabase().StringGetAsync(redisKey)).ToString());
        var pending = await InvokeAsync(key, _ => Task.CompletedTask);
        Assert.Equal(StatusCodes.Status409Conflict, pending.StatusCode);
        Assert.Equal("IDEMPOTENCY_REQUEST_PENDING", ReadErrorCode(pending.Body));

        releaseNewer.SetResult();
        var newer = await newerTask;
        Assert.Equal("newer", newer.Body);
    }

    [Fact]
    public async Task ExpiredStaleOwner_CompletionCannotFinalizeOrOverwriteNewerReservation()
    {
        var key = NewKey();
        var redisKey = (RedisKey)$"{_prefix}:idem:{key}";
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
        var stalePayload = (await _redis.GetDatabase().StringGetAsync(redisKey)).ToString();

        await ExpireAsync(redisKey);

        var newerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNewer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerInvocations = 0;
        RequestDelegate newerNext = async context =>
        {
            Interlocked.Increment(ref newerInvocations);
            newerEntered.SetResult();
            await releaseNewer.Task;
            await context.Response.WriteAsync("newer");
        };
        var newerTask = InvokeAsync(key, newerNext);
        await newerEntered.Task;
        var newerPayload = (await _redis.GetDatabase().StringGetAsync(redisKey)).ToString();

        Assert.NotEqual(ReadReservationToken(stalePayload), ReadReservationToken(newerPayload));

        releaseStale.SetResult();
        var stale = await staleTask;

        Assert.Equal("stale", stale.Body);
        Assert.Equal(newerPayload, (await _redis.GetDatabase().StringGetAsync(redisKey)).ToString());

        releaseNewer.SetResult();
        var newer = await newerTask;
        var replay = await InvokeAsync(key, newerNext);

        Assert.Equal("newer", newer.Body);
        Assert.Equal("newer", replay.Body);
        Assert.Equal(1, newerInvocations);
    }

    private async Task ExpireAsync(RedisKey redisKey)
    {
        Assert.True(await _redis.GetDatabase().KeyExpireAsync(redisKey, TimeSpan.FromMilliseconds(1)));

        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (!await _redis.GetDatabase().KeyExistsAsync(redisKey))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The controlled Redis reservation did not expire.");
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
        context.Request.Path = shape.RouteTemplate.Replace("{id}", shape.RouteValue, StringComparison.Ordinal);
        context.Request.QueryString = new QueryString(shape.Query);
        context.Request.RouteValues["id"] = shape.RouteValue;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(shape.Body));
        context.Request.ContentType = "application/json";
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(shape.SubjectClaimType, shape.Subject) }, "test"));

        var metadata = optedIn
            ? new EndpointMetadataCollection(new RequireIdempotencyAttribute
            {
                AllowRequestBody = allowRequestBody,
            })
            : EndpointMetadataCollection.Empty;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(shape.RouteTemplate),
            0,
            metadata,
            shape.RouteTemplate));

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
        return new ResponseSnapshot(context.Response.StatusCode, bytes);
    }

    private string NewKey()
    {
        var key = Guid.NewGuid().ToString("D");
        _keys.Add(key);
        return key;
    }

    private static string ReadErrorCode(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    private static string ReadReservationToken(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("reservationToken").GetString()!;
    }

    private sealed record RequestShape(
        string Method,
        string RouteTemplate,
        string RouteValue,
        string Subject,
        string Body)
    {
        public string SubjectClaimType { get; init; } = "sub";

        public string Query { get; init; } = string.Empty;

        public static RequestShape Default { get; } = new(
            HttpMethods.Post,
            "/v1/items/{id}",
            "11111111-1111-4111-8111-111111111111",
            "user-a",
            "{\"value\":1}");
    }

    private sealed record ResponseSnapshot(int StatusCode, byte[] BodyBytes)
    {
        public string Body => Encoding.UTF8.GetString(BodyBytes);
    }
}
