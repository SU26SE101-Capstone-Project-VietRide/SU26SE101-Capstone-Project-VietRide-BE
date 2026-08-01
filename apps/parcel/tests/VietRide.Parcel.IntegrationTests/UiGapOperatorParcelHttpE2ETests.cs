using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Parcel.Application.Features.Parcels.OperatorDetail;
using VietRide.Parcel.Application.Features.Parcels.OperatorList;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Parcel.IntegrationTests;

public sealed class UiGapOperatorParcelHttpE2ETests
    : IClassFixture<UiGapOperatorParcelHttpE2EFactory>
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid ParcelId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly UiGapOperatorParcelHttpE2EFactory factory;

    public UiGapOperatorParcelHttpE2ETests(UiGapOperatorParcelHttpE2EFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ListAndDetail_ReturnCompleteAdrProjectionsUsingOnlyJwtTenant()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {CreateJwt(OperatorId)}");

        using var listResponse = await client.GetAsync(
            "/v1/operator/parcels?page=1&pageSize=20&operatorId=bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

        var listBody = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, listBody);
        using (var document = JsonDocument.Parse(listBody))
        {
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var data = document.RootElement.GetProperty("data");
            data.GetProperty("totalItems").GetInt64().Should().Be(1);
            var item = data.GetProperty("items")[0];
            item.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
            item.GetProperty("trip").GetProperty("vehicle").GetProperty("licensePlate")
                .GetString().Should().Be("51A-12345");
            item.GetProperty("route").GetProperty("routeName")
                .GetString().Should().Be("HCM - Da Lat");
            item.GetProperty("sender").GetProperty("displayName")
                .GetString().Should().Be("Sender UI-24");
            item.GetProperty("recipient").GetProperty("displayName")
                .GetString().Should().Be("Recipient UI-24");
            item.GetProperty("estimatedTotalPriceVnd").GetInt64().Should().Be(120_000);
        }

        factory.Mediator.LastRequest.Should().BeOfType<GetOperatorParcelsQuery>()
            .Which.OperatorId.Should().Be(OperatorId);

        using var detailResponse = await client.GetAsync($"/v1/operator/parcels/{ParcelId:D}");

        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK, detailBody);
        using (var document = JsonDocument.Parse(detailBody))
        {
            var data = document.RootElement.GetProperty("data");
            data.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
            data.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
            data.GetProperty("deliveryMethod").GetString().Should().Be("TERMINAL_PICKUP");
            data.GetProperty("depositAmount").GetInt64().Should().Be(80_000);
            data.GetProperty("statusHistory")[0].GetProperty("status")
                .GetString().Should().Be("IN_TRANSIT");
            data.GetProperty("statusHistory")[0].GetProperty("source")
                .GetString().Should().Be("STATUS_TRIGGER");
        }

        var detailQuery = factory.Mediator.LastRequest.Should()
            .BeOfType<GetOperatorParcelDetailQuery>().Subject;
        detailQuery.ParcelId.Should().Be(ParcelId);
        detailQuery.OperatorId.Should().Be(OperatorId);
    }

    private static string CreateJwt(Guid operatorId)
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-at-least-32-chars-long-xxxxx")),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("role", "OPERATOR_ADMIN"),
                new Claim("operatorId", operatorId.ToString()),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: credentials));
    }
}

public sealed class UiGapOperatorParcelHttpE2EFactory : VietRideWebApplicationFactory
{
    public UiGapOperatorParcelMediator Mediator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMediator>();
            services.RemoveAll<ISender>();
            services.RemoveAll<IPublisher>();
            services.AddSingleton<IMediator>(Mediator);
            services.AddSingleton<ISender>(Mediator);
            services.AddSingleton<IPublisher>(Mediator);
        });
    }
}

public sealed class UiGapOperatorParcelMediator : IMediator
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid ParcelId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-30T07:00:00Z");

    public object? LastRequest { get; private set; }

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        object response = request switch
        {
            GetOperatorParcelsQuery => PagedResult<OperatorParcelListItemResponse>.Create(
                [CreateListProjection()],
                1,
                20,
                1),
            GetOperatorParcelDetailQuery => CreateDetailProjection(),
            _ => throw new NotSupportedException(request.GetType().Name),
        };
        return Task.FromResult((TResponse)response);
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(request switch
        {
            GetOperatorParcelsQuery => (object?)PagedResult<OperatorParcelListItemResponse>.Create(
                [CreateListProjection()],
                1,
                20,
                1),
            GetOperatorParcelDetailQuery => CreateDetailProjection(),
            _ => throw new NotSupportedException(request.GetType().Name),
        });
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
        => Task.CompletedTask;

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => EmptyStream<TResponse>();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default)
        => EmptyStream<object?>();

    private static OperatorParcelDetailResponse CreateDetailProjection()
        => new(CreateListProjection())
        {
            OperatorId = OperatorId,
            DeliveryMethod = "TERMINAL_PICKUP",
            DepositAmount = 80_000,
            OriginalDepositAmount = 80_000,
            DiscountAmount = 10_000,
            EstimatedGrossPriceVnd = 130_000,
            FinalGrossPriceVnd = 120_000,
            DepositPercent = 50m,
            PricePerKgVnd = 20_000,
            MinimumPriceVnd = 50_000,
            DimWeightFactor = 167m,
            SettlementPolicyVersion = 1,
            StatusHistory =
            [
                new OperatorParcelStatusHistoryItemResponse(
                    "IN_TRANSIT",
                    Now,
                    "SYSTEM",
                    null,
                    "STATUS_TRIGGER",
                    null),
            ],
        };

    private static OperatorParcelListItemResponse CreateListProjection()
    {
        var tripId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var senderId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        return new OperatorParcelListItemResponse(
            ParcelId: ParcelId,
            ParcelCode: "VRP-UI24-001",
            Status: "IN_TRANSIT",
            TripId: tripId,
            SenderUserId: senderId,
            RecipientName: "Recipient UI-24",
            RecipientPhone: "+84901234567",
            EstimatedSizeCategory: "MEDIUM",
            ActualSizeCategory: null,
            EstimatedChargeableWeightKg: 6m,
            ActualChargeableWeightKg: null,
            DepositRequiredVnd: 80_000,
            DepositPaidVnd: 80_000,
            BalanceRequiredVnd: 40_000,
            BalancePaidVnd: 0,
            RefundDueVnd: 0,
            ForfeitedDepositVnd: 0,
            LatestCheckInAt: Now,
            LoadCutoffAt: Now.AddHours(1),
            FinalPaymentDeadline: Now.AddHours(2),
            PendingActionType: null,
            PendingActionReason: null,
            PhotoUrl: "https://cdn.test/parcel.jpg",
            CreatedAt: Now.AddDays(-1),
            Trip: new OperatorParcelTripResponse(
                tripId,
                "IN_PROGRESS",
                Now.AddHours(-1),
                Now.AddHours(6),
                new OperatorParcelVehicleResponse(
                    Guid.Parse("44444444-4444-4444-8444-444444444444"),
                    "51A-12345")),
            Route: new OperatorParcelRouteResponse(
                Guid.Parse("55555555-5555-4555-8555-555555555555"),
                "HCM - Da Lat",
                "Ben xe Mien Dong",
                "Ben xe Da Lat"),
            Sender: new OperatorParcelUserResponse(
                senderId,
                "Sender UI-24",
                "+84901112223"),
            Recipient: new OperatorParcelUserResponse(
                null,
                "Recipient UI-24",
                "+84901234567"),
            SizeCategory: "MEDIUM",
            Description: "Fragile",
            EstimatedWeightKg: 6m,
            ActualWeightKg: null,
            EstimatedVolumeM3: 0.04m,
            ActualVolumeM3: null,
            EstimatedTotalPriceVnd: 120_000,
            FinalTotalPriceVnd: 120_000,
            DiscountAmountVnd: 10_000,
            RefundedAmountVnd: 0,
            UpdatedAt: Now);
    }

    private static async IAsyncEnumerable<T> EmptyStream<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
