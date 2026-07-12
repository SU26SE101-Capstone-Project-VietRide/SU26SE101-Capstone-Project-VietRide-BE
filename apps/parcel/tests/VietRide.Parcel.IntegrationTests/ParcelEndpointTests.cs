using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Parcel.IntegrationTests;

public sealed class ParcelEndpointTests : IClassFixture<VietRideWebApplicationFactory>
{
    private readonly VietRideWebApplicationFactory _factory;

    public ParcelEndpointTests(VietRideWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private HttpClient CreateAuthenticatedClient(string role, string? operatorId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {CreateJwt(role, operatorId)}");
        return client;
    }

    private static string CreateJwt(string role, string? operatorId = null)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("test-secret-at-least-32-chars-long-xxxxx"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new("role", role),
        };
        if (operatorId is not null)
            claims.Add(new Claim("operatorId", operatorId));

        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertValidationEnvelope(
        HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        response.StatusCode.Should().Be(expectedStatus);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)expectedStatus);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("meta").GetProperty("traceId").GetString().Should().NotBeNull();
        doc.RootElement.GetProperty("meta").GetProperty("timestamp").GetString().Should().NotBeNull();
    }

    private static async Task AssertForbiddenEnvelope(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(403);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static Guid NewId => Guid.NewGuid();

    // ── Auth: all endpoints require auth ─────────────────────────────

    [Fact]
    public async Task AllEndpoints_RejectAnonymous()
    {
        using var anonymous = _factory.CreateClient();

        var fareCreate = await anonymous.PostAsJsonAsync("/v1/operator/parcel-route-fares", new { });
        fareCreate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var fareList = await anonymous.GetAsync("/v1/operator/parcel-route-fares");
        fareList.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var parcelCreate = await anonymous.PostAsJsonAsync("/v1/parcels", new { });
        parcelCreate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var availableTrips = await anonymous.GetAsync("/v1/parcels/available-trips");
        availableTrips.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var received = await anonymous.GetAsync("/v1/parcels/received");
        received.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var detail = await anonymous.GetAsync("/v1/parcels/11111111-1111-1111-1111-111111111111");
        detail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var assistantUnload = await anonymous.PostAsJsonAsync(
            "/v1/assistant/parcels/11111111-1111-1111-1111-111111111111/unload",
            new { });
        assistantUnload.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var assistantTripParcels = await anonymous.GetAsync(
            "/v1/assistant/trips/11111111-1111-1111-1111-111111111111/parcels");
        assistantTripParcels.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var internalMarkLoaded = await anonymous.PostAsJsonAsync(
            "/internal/v1/parcels/11111111-1111-1111-1111-111111111111/mark-loaded",
            new { tripId = NewId, parcelCode = "VRP-001" });
        internalMarkLoaded.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Role authorization ──────────────────────────────────────────

    [Fact]
    public async Task OperatorEndpoints_RejectPassengerRole()
    {
        using var passenger = CreateAuthenticatedClient("PASSENGER");

        var fareCreate = await passenger.PostAsJsonAsync("/v1/operator/parcel-route-fares",
            new { routeId = NewId, sizeCategory = "MEDIUM", priceVnd = 50000, effectiveFrom = "2026-07-01T00:00:00Z" });
        await AssertForbiddenEnvelope(fareCreate);

        var fareList = await passenger.GetAsync("/v1/operator/parcel-route-fares");
        await AssertForbiddenEnvelope(fareList);

        using var patchContent = new StringContent("{}", Encoding.UTF8, "application/json");
        var fareUpdate = await passenger.PatchAsync("/v1/operator/parcel-route-fares/11111111-1111-1111-1111-111111111111/MEDIUM", patchContent);
        await AssertForbiddenEnvelope(fareUpdate);
    }

    [Fact]
    public async Task PassengerEndpoints_RejectOperatorRole()
    {
        using var op = CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId: NewId.ToString());

        var parcelCreate = await op.PostAsJsonAsync("/v1/parcels",
            new
            {
                tripId = NewId,
                sizeCategory = "MEDIUM",
                estimatedWeightKg = 5,
                recipient = new { fullName = "Test", phoneNumber = "0912345678" },
                deliveryMethod = "TERMINAL_PICKUP",
                paymentMethod = "VNPAY"
            });
        await AssertForbiddenEnvelope(parcelCreate);

        var availableTrips = await op.GetAsync("/v1/parcels/available-trips?originStationId=11111111-1111-1111-1111-111111111111&destinationStationId=22222222-2222-2222-2222-222222222222&departureDate=2026-07-15&estimatedWeightKg=5&sizeCategory=MEDIUM");
        await AssertForbiddenEnvelope(availableTrips);

        var received = await op.GetAsync("/v1/parcels/received");
        await AssertForbiddenEnvelope(received);
    }

    [Fact]
    public async Task AssistantUnload_RejectsPassengerRole()
    {
        using var passenger = CreateAuthenticatedClient("PASSENGER");
        passenger.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await passenger.PostAsJsonAsync(
            "/v1/assistant/parcels/11111111-1111-1111-1111-111111111111/unload",
            new { });

        await AssertForbiddenEnvelope(response);
    }

    [Fact]
    public async Task AssistantTripParcels_RejectsPassengerRole()
    {
        using var passenger = CreateAuthenticatedClient("PASSENGER");

        var response = await passenger.GetAsync(
            "/v1/assistant/trips/11111111-1111-1111-1111-111111111111/parcels");

        await AssertForbiddenEnvelope(response);
    }

    [Fact]
    public async Task AssistantTripParcels_RejectsAssistantWithoutOperatorScope()
    {
        using var assistant = CreateAuthenticatedClient("ASSISTANT");

        var response = await assistant.GetAsync(
            "/v1/assistant/trips/11111111-1111-1111-1111-111111111111/parcels?page=1&pageSize=20");

        await AssertForbiddenEnvelope(response);
    }

    [Fact]
    public async Task PublicDeliveryEndpoints_AllowAnonymousButRequireIdempotencyKey()
    {
        using var anonymous = _factory.CreateClient();

        var confirm = await anonymous.PostAsJsonAsync(
            "/v1/parcels/delivery/confirm",
            new { token = Guid.NewGuid() });
        await AssertValidationEnvelope(confirm, HttpStatusCode.UnprocessableEntity);

        var reject = await anonymous.PostAsJsonAsync(
            "/v1/parcels/delivery/reject",
            new { token = Guid.NewGuid(), rejectionReason = "damaged" });
        await AssertValidationEnvelope(reject, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task InternalMarkLoaded_RequiresInternalJwtAndIdempotencyKey()
    {
        using var client = CreateAuthenticatedClient("DRIVER");

        var response = await client.PostAsJsonAsync(
            "/internal/v1/parcels/11111111-1111-1111-1111-111111111111/mark-loaded",
            new { tripId = NewId, parcelCode = "VRP-001" });

        await AssertValidationEnvelope(response, HttpStatusCode.UnprocessableEntity);
    }

    // ── Idempotency-Key: mutations without it → 422 ─────────────────

    [Fact]
    public async Task FareCreate_MissingIdempotencyKey_Returns422_VALIDATION_ERROR()
    {
        using var client = CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId: NewId.ToString());

        var response = await client.PostAsJsonAsync("/v1/operator/parcel-route-fares",
            new { routeId = NewId, sizeCategory = "MEDIUM", priceVnd = 50000, effectiveFrom = "2026-07-01T00:00:00Z" });

        await AssertValidationEnvelope(response, HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task FareUpdate_MissingIdempotencyKey_Returns422_VALIDATION_ERROR()
    {
        using var client = CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId: NewId.ToString());

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PatchAsync(
            "/v1/operator/parcel-route-fares/11111111-1111-1111-1111-111111111111/MEDIUM", content);

        await AssertValidationEnvelope(response, HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task ParcelCreate_MissingIdempotencyKey_Returns422_VALIDATION_ERROR()
    {
        using var client = CreateAuthenticatedClient("PASSENGER");

        var response = await client.PostAsJsonAsync("/v1/parcels",
            new
            {
                tripId = NewId,
                sizeCategory = "MEDIUM",
                estimatedWeightKg = 5,
                recipient = new { fullName = "Test", phoneNumber = "0912345678" },
                deliveryMethod = "TERMINAL_PICKUP",
                paymentMethod = "VNPAY"
            });

        await AssertValidationEnvelope(response, HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task ParcelCreate_InvalidPaymentMethod_Returns422_VALIDATION_ERROR()
    {
        using var client = CreateAuthenticatedClient("PASSENGER");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync("/v1/parcels",
            new
            {
                tripId = NewId,
                sizeCategory = "MEDIUM",
                estimatedWeightKg = 5,
                recipient = new { fullName = "Test", phoneNumber = "0912345678" },
                deliveryMethod = "TERMINAL_PICKUP",
                paymentMethod = "LOL"
            });

        await AssertValidationEnvelope(response, HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    // ── Missing/Invalid input → 400 model binding error envelope ────

    [Fact]
    public async Task AvailableTrips_MissingQueryParams_Returns422_WithValidationEnvelope()
    {
        using var client = CreateAuthenticatedClient("PASSENGER");

        var response = await client.GetAsync("/v1/parcels/available-trips");

        await AssertValidationEnvelope(response, HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task AvailableTrips_InvalidDate_Returns422_WithValidationEnvelope()
    {
        using var client = CreateAuthenticatedClient("PASSENGER");

        var response = await client.GetAsync(
            "/v1/parcels/available-trips?originStationId=11111111-1111-1111-1111-111111111111&destinationStationId=22222222-2222-2222-2222-222222222222&departureDate=not-a-date&estimatedWeightKg=5&sizeCategory=MEDIUM");

        await AssertValidationEnvelope(response, HttpStatusCode.UnprocessableEntity);
    }

    // ── Fare list: auth passes, DB fails → 503 not 403/401 ──────────

    [Fact]
    public async Task FareList_WithValidAuth_DoesNotReturnAuthError()
    {
        using var client = CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId: NewId.ToString());

        var response = await client.GetAsync("/v1/operator/parcel-route-fares");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── Ping smoke ──────────────────────────────────────────────────

    [Fact]
    public async Task Ping_Returns200_WithoutAuth()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("service").GetString().Should().Be("Parcel");
    }
}
