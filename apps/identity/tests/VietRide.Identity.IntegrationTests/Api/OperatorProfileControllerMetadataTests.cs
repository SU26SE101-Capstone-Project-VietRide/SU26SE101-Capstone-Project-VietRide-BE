using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Operators;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using Xunit;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class OperatorProfileControllerMetadataTests
{
    [Fact]
    public async Task GetAsync_WhenOperatorIdClaimPresent_SendsOwnOperatorProfileQuery()
    {
        var operatorId = Guid.NewGuid();
        var mediator = new CapturingMediator();
        var controller = CreateController(mediator, operatorId, "OPERATOR_STAFF");

        var result = await controller.GetAsync(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<OperatorProfileResponse>(okResult.Value);
        Assert.Equal(operatorId, profile.OperatorId);
        Assert.Equal("VietRide Limousine", profile.Name);
        Assert.Equal("0312345678", profile.BusinessRegistrationNumber);
        Assert.Equal("0312345678", profile.TaxCode);
        Assert.Equal("ops@example.com", profile.ContactEmail);
        Assert.Equal("+84901234567", profile.ContactPhone);
        Assert.Equal("123 Le Loi", profile.Address.Street);
        Assert.Equal("Nguyen Van Operator", profile.RepresentativeName);
        Assert.True(profile.IsActive);
        Assert.IsType<GetOperatorProfileQuery>(mediator.LastRequest);
        Assert.Equal(operatorId, ((GetOperatorProfileQuery)mediator.LastRequest!).OperatorId);
    }

    [Fact]
    public async Task PatchAsync_WhenOperatorIdClaimPresent_SendsFullUpdateOperatorProfileCommand()
    {
        var operatorId = Guid.NewGuid();
        var mediator = new CapturingMediator();
        var controller = CreateController(mediator, operatorId, "OPERATOR_ADMIN");
        var cancellationPolicy = JsonDocument.Parse("[]").RootElement.Clone();
        var request = CreateRequest(cancellationPolicy);

        var result = await controller.PatchAsync(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<OperatorProfileResponse>(okResult.Value);
        Assert.Equal(operatorId, profile.OperatorId);
        var command = Assert.IsType<UpdateOperatorProfileCommand>(mediator.LastRequest);
        Assert.Equal(operatorId, command.OperatorId);
        Assert.Equal("OPERATOR_ADMIN", command.CallerRole);
        Assert.Equal(request.Name, command.Name);
        Assert.Equal(request.ContactPhone, command.ContactPhone);
        Assert.Equal(request.LogoUrl, command.LogoUrl);
        Assert.Equal(request.AddressStreet, command.AddressStreet);
        Assert.Equal(request.AddressWard, command.AddressWard);
        Assert.Equal(request.AddressProvince, command.AddressProvince);
        Assert.Equal(request.RepresentativeName, command.RepresentativeName);
        Assert.Equal(request.RepresentativePhone, command.RepresentativePhone);
        Assert.Equal(cancellationPolicy.GetRawText(), command.CancellationPolicy?.GetRawText());
    }

    [Fact]
    public async Task PatchAsync_WhenOperatorIdClaimMissing_ThrowsForbiddenWithoutSendingCommand()
    {
        var mediator = new CapturingMediator();
        var controller = CreateController(mediator, operatorId: null, "OPERATOR_ADMIN");
        var request = CreateRequest(JsonDocument.Parse("[]").RootElement.Clone());

        await Assert.ThrowsAsync<ForbiddenException>(() => controller.PatchAsync(request, CancellationToken.None));

        Assert.Null(mediator.LastRequest);
    }

    [Fact]
    public void Actions_ExposeExpectedRouteRolesEnvelopeMetadataAndDoNotUseIdPathSegment()
    {
        var controllerRoute = Assert.Single(typeof(OperatorProfileController).GetCustomAttributes(typeof(RouteAttribute), inherit: false));
        Assert.Equal("v1/operator/profile", ((RouteAttribute)controllerRoute).Template);

        var getMethod = typeof(OperatorProfileController).GetMethod(nameof(OperatorProfileController.GetAsync))!;
        var patchMethod = typeof(OperatorProfileController).GetMethod(nameof(OperatorProfileController.PatchAsync))!;

        Assert.NotNull(getMethod.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).SingleOrDefault());
        Assert.NotNull(patchMethod.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).SingleOrDefault());
        Assert.Contains("OPERATOR_ADMIN", getMethod.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Contains("OPERATOR_STAFF", getMethod.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Equal("OPERATOR_ADMIN", patchMethod.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>().Single().Roles);

        AssertProduces(getMethod, StatusCodes.Status200OK, typeof(ApiResponse<OperatorProfileResponse>));
        AssertProduces(getMethod, StatusCodes.Status403Forbidden, typeof(ApiResponse));
        AssertProduces(getMethod, StatusCodes.Status404NotFound, typeof(ApiResponse));
        AssertProduces(patchMethod, StatusCodes.Status200OK, typeof(ApiResponse<OperatorProfileResponse>));
        AssertProduces(patchMethod, StatusCodes.Status403Forbidden, typeof(ApiResponse));
        AssertProduces(patchMethod, StatusCodes.Status404NotFound, typeof(ApiResponse));
        AssertProduces(patchMethod, StatusCodes.Status422UnprocessableEntity, typeof(ApiResponse));
    }

    private static UpdateOperatorProfileRequest CreateRequest(JsonElement cancellationPolicy)
    {
        return new UpdateOperatorProfileRequest(
            "Updated Operator",
            "+84909876543",
            "https://cdn.vietride.app/operators/updated.png",
            "456 Nguyen Hue",
            "Ben Thanh",
            "Ho Chi Minh City",
            "Tran Van Admin",
            "+84901112222",
            cancellationPolicy,
            null,
            null);
    }

    private static void AssertProduces(MethodInfo method, int statusCode, Type responseType)
    {
        Assert.Contains(
            method.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false).Cast<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == statusCode && attribute.Type == responseType);
    }

    private static OperatorProfileController CreateController(CapturingMediator mediator, Guid? operatorId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
        };

        if (operatorId.HasValue)
        {
            claims.Add(new Claim("operatorId", operatorId.Value.ToString()));
        }

        return new OperatorProfileController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                },
            },
        };
    }

    private sealed class CapturingMediator : IMediator
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (request is GetOperatorProfileQuery getOperatorProfileQuery)
            {
                var response = CreateResponse(getOperatorProfileQuery.OperatorId, "PENDING");

                return Task.FromResult((TResponse)(object)response);
            }

            if (request is UpdateOperatorProfileCommand updateOperatorProfileCommand)
            {
                var response = CreateResponse(updateOperatorProfileCommand.OperatorId, "APPROVED");

                return Task.FromResult((TResponse)(object)response);
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<object?>(null);
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return EmptyAsync<TResponse>();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            return EmptyAsync<object?>();
        }

        private static OperatorProfileResponse CreateResponse(Guid operatorId, string registrationStatus)
        {
            return new OperatorProfileResponse(
                operatorId,
                "VietRide Limousine",
                "0312345678",
                "0312345678",
                "ops@example.com",
                "+84901234567",
                null,
                new OperatorProfileAddressResponse("123 Le Loi", "Ben Nghe", "Ho Chi Minh City"),
                "Nguyen Van Operator",
                "+84907654321",
                registrationStatus,
                true,
                JsonDocument.Parse("[]").RootElement.Clone(),
                JsonDocument.Parse("{\"noShowFeePercent\":0,\"additionalPaymentTimeoutMinutes\":30}").RootElement.Clone(),
                JsonDocument.Parse("{\"defaultLuggageKgPerSeat\":10}").RootElement.Clone());
        }

        private static async IAsyncEnumerable<TResponse> EmptyAsync<TResponse>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
