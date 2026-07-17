using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Application.Features.Admin.ListActivityLogs;
using VietRide.Identity.Application.Features.Internal.Operators.GetOperatorSummaries;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.IntegrationTests.Api;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.IntegrationTests;

public sealed class ActivityLogImmutabilityTests
{
    [Fact]
    public async Task ReadModelAndDatabaseAppendOnlyInvariants_HoldOnPostgreSql()
    {
        using var factory = new AdminUsersEndpointsTests.DbBackedAdminUsersFactory();
        try
        {
            await factory.InitializeAsync();
            var fixture = await SeedAsync(factory);

            await AssertReadModelAsync(factory, fixture);
            await AssertOperatorSummariesAsync(factory, fixture);
            await AssertHttpContractsAsync(factory, fixture);
            await AssertSourceEventUniquenessAsync(factory, fixture.ActorId);
            await AssertMutationRejectedAsync(factory, fixture.ToLogId, "UPDATE");
            await AssertMutationRejectedAsync(factory, fixture.ToLogId, "DELETE");
            await AssertSchemaObjectsAsync(factory);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    private static async Task<Fixture> SeedAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var actor = User.CreatePassenger(
            "activity-actor@example.com",
            PhoneNumber.Parse("+84901234567"),
            "hash",
            "Deleted Actor");
        actor.VerifyEmail();
        actor.SoftDelete(DateTimeOffset.UtcNow);
        var activeOperator = CreateOperator("Active Operator");
        var deletedOperator = CreateOperator("Deleted Operator");
        deletedOperator.SoftDelete(DateTimeOffset.UtcNow);
        await db.Users.AddAsync(actor);
        await db.Operators.AddRangeAsync(activeOperator, deletedOperator);
        await db.SaveChangesAsync();

        var from = new DateTimeOffset(2026, 7, 16, 1, 0, 0, TimeSpan.Zero);
        var middle = from.AddMinutes(1);
        var to = from.AddMinutes(2);
        var fromLogId = Guid.Parse("40000000-0000-0000-0000-000000000000");
        var lowerId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("40000000-0000-0000-0000-000000000002");
        var toLogId = Guid.Parse("40000000-0000-0000-0000-000000000003");
        await InsertLogAsync(db, fromLogId, actor.Id, from, "{\"sequence\":0}");
        await InsertLogAsync(db, lowerId, actor.Id, middle, "{\"sequence\":1}");
        await InsertLogAsync(db, higherId, actor.Id, middle, "{\"sequence\":2}");
        await InsertLogAsync(db, toLogId, actor.Id, to, "{\"sequence\":3}");

        return new Fixture(
            actor.Id,
            activeOperator.Id,
            deletedOperator.Id,
            from,
            to,
            fromLogId,
            lowerId,
            higherId,
            toLogId);
    }

    private static async Task InsertLogAsync(
        IdentityDbContext db,
        Guid id,
        Guid actorId,
        DateTimeOffset createdAt,
        string metadata)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO vietride_identity.activity_logs (id, user_id, action, metadata, created_at) VALUES ({id}, {actorId}, 'LOCK_USER', {metadata}::jsonb, {createdAt})");
    }

    private static async Task AssertReadModelAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        Fixture fixture)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new ListActivityLogsQuery(
            UserRole.SYSTEM_ADMIN.ToString(),
            fixture.ActorId,
            ActivityLogAction.LOCK_USER.ToString(),
            fixture.From,
            fixture.To,
            1,
            20));

        result.TotalItems.Should().Be(3);
        result.Items.Select(item => item.Id).Should().Equal(
            fixture.HigherId,
            fixture.LowerId,
            fixture.FromLogId);
        result.Items.Should().OnlyContain(item => item.Actor.Id == fixture.ActorId);
        result.Items.Should().OnlyContain(item => item.Actor.DisplayName == "Deleted Actor");
        result.Items[0].Metadata!.Value.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
    }

    private static async Task AssertOperatorSummariesAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        Fixture fixture)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetOperatorSummariesQuery(
            [fixture.DeletedOperatorId, Guid.NewGuid(), fixture.ActiveOperatorId]));

        result.Select(item => item.OperatorId).Should().Equal(
            new[] { fixture.ActiveOperatorId, fixture.DeletedOperatorId }.OrderBy(id => id));
        result.Select(item => item.OperatorName).Should().Contain("Deleted Operator");
    }

    private static async Task AssertHttpContractsAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        Fixture fixture)
    {
        using var client = factory.CreateClient();
        var from = Uri.EscapeDataString(fixture.From.ToString("O"));
        var to = Uri.EscapeDataString(fixture.To.ToString("O"));
        using var activityRequest = CreateInternalJwtRequest(
            HttpMethod.Get,
            $"/v1/admin/activity-logs?userId={fixture.ActorId}&action=LOCK_USER&from={from}&to={to}",
            UserRole.SYSTEM_ADMIN.ToString());
        var activityResponse = await client.SendAsync(activityRequest);

        activityResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var activityDocument = JsonDocument.Parse(await activityResponse.Content.ReadAsStringAsync()))
        {
            activityDocument.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var data = activityDocument.RootElement.GetProperty("data");
            data.GetProperty("totalItems").GetInt64().Should().Be(3);
            data.GetProperty("items")[0].GetProperty("metadata").ValueKind
                .Should().Be(JsonValueKind.Object);
        }

        using var forbiddenRequest = CreateInternalJwtRequest(
            HttpMethod.Get,
            "/v1/admin/activity-logs",
            UserRole.PASSENGER.ToString());
        var forbiddenResponse = await client.SendAsync(forbiddenRequest);
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var anonymousSummaryResponse = await client.PostAsJsonAsync(
            "/internal/v1/operators/summaries/batch",
            new { operatorIds = new[] { fixture.ActiveOperatorId } });
        anonymousSummaryResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var summaryRequest = CreateInternalJwtRequest(
            HttpMethod.Post,
            "/internal/v1/operators/summaries/batch",
            UserRole.SYSTEM_ADMIN.ToString());
        summaryRequest.Content = JsonContent.Create(new
        {
            operatorIds = new[] { fixture.DeletedOperatorId, fixture.ActiveOperatorId },
        });
        var summaryResponse = await client.SendAsync(summaryRequest);

        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaryJson = await summaryResponse.Content.ReadAsStringAsync();
        summaryJson.Should().NotContain("\"success\"");
        using var summaryDocument = JsonDocument.Parse(summaryJson);
        summaryDocument.RootElement.GetArrayLength().Should().Be(2);
        summaryDocument.RootElement.EnumerateArray().Select(item => item.GetProperty("operatorId").GetGuid())
            .Should().BeInAscendingOrder();
        summaryDocument.RootElement.EnumerateArray().Select(item => item.GetProperty("operatorName").GetString())
            .Should().Contain("Deleted Operator");
    }

    private static HttpRequestMessage CreateInternalJwtRequest(
        HttpMethod method,
        string path,
        string role)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("role", role),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {new JwtSecurityTokenHandler().WriteToken(token)}");
        return request;
    }

    private static async Task AssertSourceEventUniquenessAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        Guid actorId)
    {
        var sourceEventId = Guid.NewGuid();
        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var firstDb = firstScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await firstDb.ActivityLogs.AddAsync(ActivityLog.Create(
                actorId,
                ActivityLogAction.LOCK_USER,
                sourceEventId: sourceEventId));
            await firstDb.SaveChangesAsync();
        }

        await using var secondScope = factory.Services.CreateAsyncScope();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await secondDb.ActivityLogs.AddAsync(ActivityLog.Create(
            actorId,
            ActivityLogAction.LOCK_USER,
            sourceEventId: sourceEventId));
        var act = () => secondDb.SaveChangesAsync();
        var assertion = await act.Should().ThrowAsync<DbUpdateException>();
        assertion.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private static async Task AssertMutationRejectedAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        Guid activityLogId,
        string mutation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Func<Task<int>> act = mutation == "UPDATE"
            ? () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE vietride_identity.activity_logs SET metadata = '{{}}'::jsonb WHERE id = {activityLogId}")
            : () => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM vietride_identity.activity_logs WHERE id = {activityLogId}");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(exception => exception.SqlState == "55000");
    }

    private static async Task AssertSchemaObjectsAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'vietride_identity' AND indexname IN ('idx_activity_logs_created_at_id', 'uq_activity_logs_source_event_id')";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(2);
        command.CommandText =
            "SELECT COUNT(*) FROM pg_trigger WHERE tgname = 'trg_activity_logs_append_only' AND NOT tgisinternal";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(1);
    }

    private static Operator CreateOperator(string name)
        => Operator.CreatePending(
            name,
            $"BR-{Guid.NewGuid():N}",
            $"TAX-{Guid.NewGuid():N}",
            $"{Guid.NewGuid():N}@example.com",
            "+84901234567");

    private sealed record Fixture(
        Guid ActorId,
        Guid ActiveOperatorId,
        Guid DeletedOperatorId,
        DateTimeOffset From,
        DateTimeOffset To,
        Guid FromLogId,
        Guid LowerId,
        Guid HigherId,
        Guid ToLogId);
}
