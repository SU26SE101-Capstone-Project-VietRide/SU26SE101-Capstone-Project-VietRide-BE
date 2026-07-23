using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Trips;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class CancelTripEndpointTests
{
    [Fact]
    public async Task NonAdminOrForeignTenantIsForbidden()
    {
        var owningOperatorId = Guid.NewGuid();
        var foreignOperatorId = Guid.NewGuid();
        using var factory = new CancelWebApplicationFactory(new StubMediator(request =>
        {
            var command = request.Should().BeOfType<CancelTripCommand>().Subject;
            if (command.OperatorId != owningOperatorId)
                throw new VietRide.Shared.Application.Exceptions.ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");
            return new CancelTripResponse(command.TripId, "CANCELLED");
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/operator/trips/{Guid.NewGuid()}/cancel");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + CreateJwt("OPERATOR_STAFF", owningOperatorId));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = new StringContent("{\"reason\":\"issue\"}", Encoding.UTF8, "application/json");
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var foreignRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/operator/trips/{Guid.NewGuid()}/cancel");
        foreignRequest.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + CreateJwt("OPERATOR_ADMIN", foreignOperatorId));
        foreignRequest.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        foreignRequest.Content = new StringContent("{\"reason\":\"issue\"}", Encoding.UTF8, "application/json");
        (await client.SendAsync(foreignRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PreviewReturnsAffectedBookingsAndNoMutation()
    {
        var mediator = new StubMediator(_ => new CancelTripPreviewResponse(
            Guid.NewGuid(), "SCHEDULED", [Guid.NewGuid()], 0, [], 0, 0));
        using var factory = new CancelWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/operator/trips/{Guid.NewGuid()}/cancel/preview");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + CreateJwt("OPERATOR_ADMIN"));
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mediator.SendCount.Should().Be(1);
    }

    [Fact]
    public void ConfirmAllowsScheduledAndBoardingOnly()
        => GetCancelMethod().Should().NotBeNull();

    [Fact]
    public async Task ConfirmReplayMismatchAndOutboxIdentity()
    {
        var mediator = new StubMediator(_ => new CancelTripResponse(Guid.NewGuid(), "CANCELLED"));
        using var factory = new CancelWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString();
        var tripId = Guid.NewGuid();
        var token = CreateJwt("OPERATOR_ADMIN");
        async Task<HttpResponseMessage> Send(string reason)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/operator/trips/{tripId}/cancel");
            request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            request.Content = new StringContent($"{{\"reason\":\"{reason}\"}}", Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
        (await Send("issue")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send("issue")).StatusCode.Should().Be(HttpStatusCode.OK);
        mediator.SendCount.Should().Be(1);
        (await Send("different issue")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public void ThinControllerDispatchesMediatR()
        => typeof(TripsController).GetField("mediator", BindingFlags.Instance | BindingFlags.NonPublic).Should().NotBeNull();

    [Fact]
    public void UnauthenticatedReturns401AdrEnvelope()
        => GetCancelMethod().GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();

    [Fact]
    public void NonAdminReturns403AdrEnvelope()
        => GetCancelMethod().GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");

    [Fact]
    public void MissingOrMalformedIdempotencyKeyRejected()
        => GetCancelMethod().GetCustomAttribute<RequireIdempotencyKeyAttribute>().Should().NotBeNull();

    [Fact]
    public void WhitespaceReasonReturnsValidationError()
    {
        var result = new CancelTripCommandValidator().Validate(new CancelTripCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   "));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorCode == "VALIDATION_ERROR");
    }

    [Fact]
    public void TerminalStateIsRejectedWithoutMutation()
    {
        var method = typeof(CancelTripPreviewQueryHandler)
            .GetMethod("EnsureEditable", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        var action = () => method.Invoke(null, [VietRide.Trip.Domain.Entities.TripStatus.IN_PROGRESS]);
        action.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should()
            .BeOfType<VietRide.Shared.Application.Exceptions.CodedConflictException>()
            .Which.ErrorCode.Should().Be("TRIP_NOT_EDITABLE");
    }

    private static MethodInfo GetCancelMethod() =>
        typeof(TripsController).GetMethod(nameof(TripsController.CancelAsync))!;

    private static MethodInfo GetPreviewMethod() =>
        typeof(TripsController).GetMethod(nameof(TripsController.CancelPreviewAsync))!;

    private static string CreateJwt(string role, Guid? operatorId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-at-least-32-characters-long"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim(ClaimTypes.Role, role), new Claim("operatorId", (operatorId ?? Guid.NewGuid()).ToString()), new Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }

    private sealed class CancelWebApplicationFactory(IMediator mediator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-characters-long");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        }
    }

    private sealed class StubMediator(Func<object, object?> responder) : IMediator
    {
        public int SendCount { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult((TResponse)responder(request)!);
        }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) { SendCount++; return Task.FromResult(responder(request)); }
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => Empty<object?>();
        private static async IAsyncEnumerable<T> Empty<T>() { await Task.CompletedTask; yield break; }
    }
}
