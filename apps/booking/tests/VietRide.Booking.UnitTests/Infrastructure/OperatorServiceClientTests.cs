using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VietRide.Booking.Infrastructure.Http;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class OperatorServiceClientTests
{
    private static readonly Guid OperatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetOperatorAsync_Returns_Operator_WithNameAndCancellationPolicy_On_200()
    {
        var body = JsonSerializer.Serialize(new
        {
            operatorId = OperatorId,
            name = "VietRide Limousine",
            registrationStatus = "APPROVED",
            isActive = true,
            contactEmail = "ops@example.com",
            contactPhone = "+84901234567",
            businessRegistrationNumber = "0312345678",
            taxCode = "0312345678",
            cancellationPolicy = new[]
            {
                new { hoursBeforeDeparture = 24, feePercent = 10 },
            },
        }, JsonOptions);
        var handler = new FakeMessageHandler(HttpStatusCode.OK, body);
        var client = BuildClient(handler);

        var result = await client.GetOperatorAsync(OperatorId);

        result.Should().NotBeNull();
        result!.OperatorId.Should().Be(OperatorId);
        result.Name.Should().Be("VietRide Limousine");
        result.CancellationPolicy.Should().NotBeNull();
        result.CancellationPolicy!.Value[0].GetProperty("hoursBeforeDeparture").GetInt32().Should().Be(24);
        result.CancellationPolicy!.Value[0].GetProperty("feePercent").GetInt32().Should().Be(10);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be($"/internal/v1/operators/{OperatorId:D}");
    }

    [Fact]
    public async Task GetOperatorAsync_Returns_Null_On_404()
    {
        var client = BuildClient(new FakeMessageHandler(HttpStatusCode.NotFound, "{}"));

        var result = await client.GetOperatorAsync(OperatorId);

        result.Should().BeNull();
    }

    private static OperatorServiceClient BuildClient(FakeMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://identity-service"),
        };
        return new OperatorServiceClient(httpClient);
    }

    private sealed class FakeMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeMessageHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
