using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Middleware;

public sealed class IdempotencyMiddlewareTests
{
    private const string Prefix = "svc";
    private const string Key = "11111111-1111-4111-8111-111111111111";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task MissingRequiredHeader_ReturnsExactRequiredError()
    {
        var (mux, db) = FakeRedis();
        var invoked = 0;
        var context = BuildContext(key: null, optedIn: true);

        await Create(_ =>
        {
            invoked++;
            return Task.CompletedTask;
        }, mux).InvokeAsync(context);

        invoked.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReadErrorCode(context).Should().Be(IdempotencyMiddleware.RequiredErrorCode);
        await db.DidNotReceive().KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task ReadMethod_PassesThroughWithoutRedis(string method)
    {
        var (mux, db) = FakeRedis();
        var invoked = 0;
        var context = BuildContext(method: method, optedIn: false);

        await Create(_ =>
        {
            invoked++;
            return Task.CompletedTask;
        }, mux).InvokeAsync(context);

        invoked.Should().Be(1);
        await db.DidNotReceive().KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task MutationWithKey_UsesV2ProcessingLock(string method)
    {
        var (mux, db) = FakeRedis();
        ConfigureEmptyV2(db);
        var context = BuildContext(method: method, optedIn: false);

        await Create(c => c.Response.WriteAsync("ok"), mux).InvokeAsync(context);

        await db.Received(1).StringSetAsync(
            ProcessingKey(Key),
            Arg.Any<RedisValue>(),
            TimeSpan.FromSeconds(120),
            When.NotExists,
            CommandFlags.None);
    }

    [Fact]
    public async Task FirstRequest_StoresResponseAndReplayPreservesStatusBodyAndContentType()
    {
        var (mux, db) = FakeRedis();
        RedisValue cachedResponse = RedisValue.Null;
        ConfigureStatefulSuccess(db, value => cachedResponse = value);
        var invoked = 0;
        RequestDelegate next = async context =>
        {
            invoked++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/vnd.vietride+json; charset=utf-8";
            await context.Response.WriteAsync("{\"created\":true}");
        };

        var first = BuildContext();
        await Create(next, mux).InvokeAsync(first);
        var replay = BuildContext();
        db.StringGetAsync(ResponseKey(Key), CommandFlags.None).Returns(cachedResponse);
        await Create(next, mux).InvokeAsync(replay);

        invoked.Should().Be(1);
        replay.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        replay.Response.ContentType.Should().Be("application/vnd.vietride+json; charset=utf-8");
        ReadResponse(replay).Should().Be("{\"created\":true}");
    }

    [Fact]
    public async Task ResponseCompletedBeforeProcessingAcquisition_ReplaysWithoutExecutingDownstream()
    {
        var (mux, db) = FakeRedis();
        db.KeyExistsAsync(LegacyKey(Key), CommandFlags.None).Returns(false);
        RedisValue processingPayload = RedisValue.Null;
        var responseReads = 0;
        db.StringGetAsync(ResponseKey(Key), CommandFlags.None).Returns(_ =>
        {
            responseReads++;
            if (responseReads == 1)
            {
                return RedisValue.Null;
            }

            using var processing = JsonDocument.Parse(processingPayload.ToString());
            var fingerprint = processing.RootElement.GetProperty("requestFingerprint").GetString();
            return JsonSerializer.Serialize(new
            {
                requestFingerprint = fingerprint,
                statusCode = StatusCodes.Status201Created,
                contentType = "application/json",
                body = Convert.ToBase64String(Encoding.UTF8.GetBytes("cached")),
            });
        });
        db.StringSetAsync(
                ProcessingKey(Key),
                Arg.Any<RedisValue>(),
                TimeSpan.FromSeconds(120),
                When.NotExists,
                CommandFlags.None)
            .Returns(call =>
            {
                processingPayload = call.ArgAt<RedisValue>(1);
                return true;
            });
        var invoked = 0;
        var context = BuildContext();

        await Create(_ =>
        {
            invoked++;
            return Task.CompletedTask;
        }, mux).InvokeAsync(context);

        invoked.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        ReadResponse(context).Should().Be("cached");
        await db.Received(1).ScriptEvaluateAsync(
            Arg.Is<string>(script => script.Contains("DEL", StringComparison.Ordinal)),
            Arg.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == ProcessingKey(Key)),
            Arg.Any<RedisValue[]>(),
            CommandFlags.None);
    }

    [Theory]
    [InlineData("/v1/items/other", "", "{\"value\":1}", "user-a")]
    [InlineData("/v1/items/1", "?a=1", "{\"value\":1}", "user-a")]
    [InlineData("/v1/items/1", "", "{ \"value\": 1 }", "user-a")]
    [InlineData("/v1/items/1", "", "{\"value\":1}", "user-b")]
    public async Task SameKeyWithDifferentFingerprint_ReturnsMismatch(
        string path,
        string query,
        string body,
        string subject)
    {
        var (mux, db) = FakeRedis();
        RedisValue cachedResponse = RedisValue.Null;
        ConfigureStatefulSuccess(db, value => cachedResponse = value);
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("original");
        };

        await Create(next, mux).InvokeAsync(BuildContext());
        db.StringGetAsync(ResponseKey(Key), CommandFlags.None).Returns(cachedResponse);
        var mismatch = BuildContext(path: path, query: query, body: body, subject: subject);
        await Create(next, mux).InvokeAsync(mismatch);

        invoked.Should().Be(1);
        mismatch.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        ReadErrorCode(mismatch).Should().Be(IdempotencyMiddleware.MismatchErrorCode);
    }

    [Fact]
    public async Task CanonicalQuery_SortsKeysAndDuplicateValues()
    {
        var (mux, db) = FakeRedis();
        RedisValue cachedResponse = RedisValue.Null;
        ConfigureStatefulSuccess(db, value => cachedResponse = value);
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("same-query");
        };

        await Create(next, mux).InvokeAsync(BuildContext(query: "?b=2&a=3&a=1"));
        db.StringGetAsync(ResponseKey(Key), CommandFlags.None).Returns(cachedResponse);
        var replay = BuildContext(query: "?a=1&b=2&a=3");
        await Create(next, mux).InvokeAsync(replay);

        invoked.Should().Be(1);
        ReadResponse(replay).Should().Be("same-query");
    }

    [Fact]
    public async Task EmptyBodySameKeyDifferentPath_ReturnsMismatch()
    {
        var (mux, db) = FakeRedis();
        RedisValue cachedResponse = RedisValue.Null;
        ConfigureStatefulSuccess(db, value => cachedResponse = value);
        var invoked = 0;
        RequestDelegate next = context =>
        {
            invoked++;
            return context.Response.WriteAsync("arrived");
        };

        await Create(next, mux).InvokeAsync(BuildContext(path: "/v1/trips/1/stops/1/arrive", body: string.Empty));
        db.StringGetAsync(ResponseKey(Key), CommandFlags.None).Returns(cachedResponse);
        var mismatch = BuildContext(path: "/v1/trips/2/stops/1/arrive", body: string.Empty);
        await Create(next, mux).InvokeAsync(mismatch);

        invoked.Should().Be(1);
        ReadErrorCode(mismatch).Should().Be(IdempotencyMiddleware.MismatchErrorCode);
    }

    [Fact]
    public async Task ConcurrentSameFingerprint_ReturnsPendingWithoutDownstreamExecution()
    {
        var (mux, db) = FakeRedis();
        RedisValue processing = RedisValue.Null;
        db.KeyExistsAsync(LegacyKey(Key), CommandFlags.None).Returns(false);
        db.StringGetAsync(ResponseKey(Key), CommandFlags.None).Returns(RedisValue.Null);
        db.StringSetAsync(
                ProcessingKey(Key),
                Arg.Any<RedisValue>(),
                TimeSpan.FromSeconds(120),
                When.NotExists,
                CommandFlags.None)
            .Returns(call =>
            {
                processing = call.ArgAt<RedisValue>(1);
                return false;
            });
        db.StringGetAsync(ProcessingKey(Key), CommandFlags.None).Returns(_ => processing);
        var invoked = 0;
        var context = BuildContext();

        await Create(_ =>
        {
            invoked++;
            return Task.CompletedTask;
        }, mux).InvokeAsync(context);

        invoked.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        ReadErrorCode(context).Should().Be(IdempotencyMiddleware.PendingErrorCode);
    }

    [Fact]
    public async Task FiveHundredResponse_ReleasesProcessingAndDoesNotCacheResponse()
    {
        var (mux, db) = FakeRedis();
        ConfigureEmptyV2(db);
        var context = BuildContext();

        await Create(async c =>
        {
            c.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await c.Response.WriteAsync("temporary");
        }, mux).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        ReadResponse(context).Should().Be("temporary");
        await db.Received(1).ScriptEvaluateAsync(
            Arg.Is<string>(script => script.Contains("DEL", StringComparison.Ordinal)),
            Arg.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == ProcessingKey(Key)),
            Arg.Any<RedisValue[]>(),
            CommandFlags.None);
        await db.DidNotReceive().StringSetAsync(
            ResponseKey(Key),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task NullScriptResult_FallsBackWithoutBreakingTheResponse()
    {
        var (mux, db) = FakeRedis();
        ConfigureEmptyV2(db);
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult<RedisResult>(null!));
        var context = BuildContext();

        await Create(async c =>
        {
            c.Response.StatusCode = StatusCodes.Status201Created;
            await c.Response.WriteAsync("created");
        }, mux).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        ReadResponse(context).Should().Be("created");
    }

    [Fact]
    public async Task LegacyEntry_FailsClosedWithoutDeletingIt()
    {
        var (mux, db) = FakeRedis();
        db.KeyExistsAsync(LegacyKey(Key), CommandFlags.None).Returns(true);
        var invoked = 0;
        var context = BuildContext();

        await Create(_ =>
        {
            invoked++;
            return Task.CompletedTask;
        }, mux).InvokeAsync(context);

        invoked.Should().Be(0);
        ReadErrorCode(context).Should().Be(IdempotencyMiddleware.MismatchErrorCode);
        await db.DidNotReceive().KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    private static (IConnectionMultiplexer Mux, IDatabase Db) FakeRedis()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1));
        return (mux, db);
    }

    private static void ConfigureEmptyV2(IDatabase db)
    {
        db.KeyExistsAsync(LegacyKey(Key), CommandFlags.None).Returns(false);
        db.StringGetAsync(ResponseKey(Key), CommandFlags.None).Returns(RedisValue.Null);
        db.StringSetAsync(
                ProcessingKey(Key),
                Arg.Any<RedisValue>(),
                TimeSpan.FromSeconds(120),
                When.NotExists,
                CommandFlags.None)
            .Returns(true);
    }

    private static void ConfigureStatefulSuccess(IDatabase db, Action<RedisValue> cacheResponse)
    {
        ConfigureEmptyV2(db);
        db.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Is<RedisKey[]>(keys => keys.Length == 2 && keys[1] == ResponseKey(Key)),
                Arg.Any<RedisValue[]>(),
                CommandFlags.None)
            .Returns(call =>
            {
                cacheResponse(call.ArgAt<RedisValue[]>(2)[2]);
                return RedisResult.Create((RedisValue)1);
            });
    }

    private static DefaultHttpContext BuildContext(
        string method = "POST",
        string? key = Key,
        string path = "/v1/items/1",
        string query = "",
        string body = "{\"value\":1}",
        string subject = "user-a",
        bool optedIn = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", subject)], "test"));
        if (key is not null)
        {
            context.Request.Headers[IdempotencyMiddleware.IdempotencyKeyHeader] = key;
        }

        if (optedIn)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new RequireIdempotencyAttribute()),
                "test"));
        }

        return context;
    }

    private static IdempotencyMiddleware Create(RequestDelegate next, IConnectionMultiplexer mux)
        => new(
            next,
            mux,
            new IdempotencyOptions { ServicePrefix = Prefix },
            NullLogger<IdempotencyMiddleware>.Instance);

    private static RedisKey LegacyKey(string key) => $"{Prefix}:idem:{key}";

    private static RedisKey ResponseKey(string key)
        => $"{Prefix}:idem:v2:response:{HashKey(key)}";

    private static RedisKey ProcessingKey(string key)
        => $"{Prefix}:idem:v2:processing:{HashKey(key)}";

    private static string HashKey(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static string ReadErrorCode(HttpContext context)
    {
        using var document = JsonDocument.Parse(ReadResponse(context));
        return document.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }
}
