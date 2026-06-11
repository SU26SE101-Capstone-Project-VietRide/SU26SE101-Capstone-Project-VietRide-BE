using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Middleware;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Middleware;

public sealed class IdempotencyMiddlewareTests
{
    private const string Prefix = "svc";
    private const string Key = "abc-123";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string RedisKey(string key) => $"{Prefix}:idem:{key}";

    private static string HashOf(string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    private static (IConnectionMultiplexer Mux, IDatabase Db) FakeRedis()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        return (mux, db);
    }

    private static DefaultHttpContext BuildContext(string method, string? key, string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        if (key is not null)
        {
            ctx.Request.Headers[IdempotencyMiddleware.IdempotencyKeyHeader] = key;
        }

        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static IdempotencyMiddleware Create(RequestDelegate next, IConnectionMultiplexer mux)
        => new(next, mux, new IdempotencyOptions { ServicePrefix = Prefix }, NullLogger<IdempotencyMiddleware>.Instance);

    private static string ReadResponse(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------
    // 1. First request (key absent from Redis) → downstream invoked + SETNX
    // ------------------------------------------------------------------

    [Fact]
    public async Task FirstRequest_Invokes_Downstream_And_Stores_With_Setnx_And_24h_Ttl()
    {
        var (mux, db) = FakeRedis();
        db.StringGetAsync(RedisKey(Key)).Returns(RedisValue.Null);

        var downstreamCalled = false;
        RequestDelegate next = async c =>
        {
            downstreamCalled = true;
            c.Response.StatusCode = 201;
            await c.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("created"));
        };

        var ctx = BuildContext("POST", Key, "{\"a\":1}");
        await Create(next, mux).InvokeAsync(ctx);

        downstreamCalled.Should().BeTrue();
        ReadResponse(ctx).Should().Be("created");
        ctx.Response.StatusCode.Should().Be(201);

        await db.Received(1).StringSetAsync(
            RedisKey(Key),
            Arg.Any<RedisValue>(),
            TimeSpan.FromSeconds(86400),
            When.NotExists,
            Arg.Any<CommandFlags>());
    }

    // ------------------------------------------------------------------
    // 2. Same key + same body hash → cached response replayed verbatim
    // ------------------------------------------------------------------

    [Fact]
    public async Task SameKey_SameBodyHash_Replays_Cached_Response_Without_Downstream()
    {
        var body = "{\"a\":1}";
        var (mux, db) = FakeRedis();

        var cached = new
        {
            statusCode = 201,
            body = Convert.ToBase64String(Encoding.UTF8.GetBytes("cached-body")),
            bodyHash = HashOf(body),
        };
        db.StringGetAsync(RedisKey(Key)).Returns(JsonSerializer.Serialize(cached, JsonOptions));

        var downstreamCalled = false;
        RequestDelegate next = c =>
        {
            downstreamCalled = true;
            return Task.CompletedTask;
        };

        var ctx = BuildContext("POST", Key, body);
        await Create(next, mux).InvokeAsync(ctx);

        downstreamCalled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(201);
        ReadResponse(ctx).Should().Be("cached-body");

        await db.DidNotReceive().StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    // ------------------------------------------------------------------
    // 3. Same key + different body hash → 422 IDEMPOTENCY_KEY_MISMATCH
    // ------------------------------------------------------------------

    [Fact]
    public async Task SameKey_DifferentBodyHash_Returns_422_Mismatch_Without_Downstream()
    {
        var (mux, db) = FakeRedis();

        var cached = new
        {
            statusCode = 201,
            body = Convert.ToBase64String(Encoding.UTF8.GetBytes("orig")),
            bodyHash = HashOf("{\"a\":1}"),
        };
        db.StringGetAsync(RedisKey(Key)).Returns(JsonSerializer.Serialize(cached, JsonOptions));

        var downstreamCalled = false;
        RequestDelegate next = c =>
        {
            downstreamCalled = true;
            return Task.CompletedTask;
        };

        var ctx = BuildContext("POST", Key, "{\"a\":999}"); // different body
        ctx.Items[RequestLoggingMiddleware.RequestIdHeader] = "trace-xyz";
        await Create(next, mux).InvokeAsync(ctx);

        downstreamCalled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(422);

        using var doc = JsonDocument.Parse(ReadResponse(ctx));
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("statusCode").GetInt32().Should().Be(422);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_MISMATCH");
        root.GetProperty("meta").GetProperty("traceId").GetString().Should().Be("trace-xyz");
    }

    // ------------------------------------------------------------------
    // 4. Missing header → pass-through, no Redis interaction
    // ------------------------------------------------------------------

    [Fact]
    public async Task MissingHeader_PassesThrough_With_No_Redis_Interaction()
    {
        var (mux, db) = FakeRedis();

        var downstreamCalled = false;
        RequestDelegate next = c =>
        {
            downstreamCalled = true;
            return Task.CompletedTask;
        };

        var ctx = BuildContext("POST", key: null, body: "{}");
        await Create(next, mux).InvokeAsync(ctx);

        downstreamCalled.Should().BeTrue();
        await db.DidNotReceive().StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
        await db.DidNotReceive().StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    // ------------------------------------------------------------------
    // 5. Non-POST/PATCH method → pass-through, no Redis interaction
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetRequest_PassesThrough_Even_With_Key()
    {
        var (mux, db) = FakeRedis();

        var downstreamCalled = false;
        RequestDelegate next = c =>
        {
            downstreamCalled = true;
            return Task.CompletedTask;
        };

        var ctx = BuildContext("GET", Key, "{}");
        await Create(next, mux).InvokeAsync(ctx);

        downstreamCalled.Should().BeTrue();
        await db.DidNotReceive().StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }
}
