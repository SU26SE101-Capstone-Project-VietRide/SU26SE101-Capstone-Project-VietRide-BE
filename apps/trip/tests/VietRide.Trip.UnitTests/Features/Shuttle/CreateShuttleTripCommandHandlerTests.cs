using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Shuttle;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Shuttle;

public sealed class CreateShuttleTripCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenOperatorAndShuttleEntitlementAreValid_DispatchesShuttle()
    {
        var command = CreateCommand();
        bool? requireShuttleModule = null;
        var identityClient = TestProxy<IIdentityInternalClient>.Create((method, args) =>
        {
            if (method.Name == nameof(IIdentityInternalClient.ValidateOperatorSubscriptionCanWriteAsync))
                requireShuttleModule = Assert.IsType<bool>(args![1]);

            return OperatorWriteEligibilityValidation.Allowed();
        });
        var expected = new CreateShuttleTripResult(
            Guid.NewGuid(),
            command.MainTripId,
            command.OrderedBookingIds.Count,
            0);
        var dispatchCalls = 0;
        var service = TestProxy<IShuttleDispatchService>.Create((method, args) =>
        {
            if (method.Name != nameof(IShuttleDispatchService.CreateAsync))
                return null;

            dispatchCalls++;
            var input = Assert.IsType<CreateShuttleTripInput>(args![0]);
            Assert.Equal(command.OperatorId, input.OperatorId);
            Assert.Equal(command.MainTripId, input.MainTripId);
            return expected;
        });
        var handler = new CreateShuttleTripCommandHandler(identityClient, service);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().Be(expected);
        requireShuttleModule.Should().BeTrue();
        dispatchCalls.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenOperatorIsNotApproved_BlocksBeforeSubscriptionAndDispatch()
    {
        var subscriptionCalls = 0;
        var dispatchCalls = 0;
        var identityClient = TestProxy<IIdentityInternalClient>.Create((method, _) =>
        {
            if (method.Name == nameof(IIdentityInternalClient.ValidateOperatorSubscriptionCanWriteAsync))
            {
                subscriptionCalls++;
                return OperatorWriteEligibilityValidation.Allowed();
            }

            return OperatorWriteEligibilityValidation.Forbidden("Operator is not approved.");
        });
        var service = TestProxy<IShuttleDispatchService>.Create((method, _) =>
        {
            if (method.Name == nameof(IShuttleDispatchService.CreateAsync))
                dispatchCalls++;

            return null;
        });
        var handler = new CreateShuttleTripCommandHandler(identityClient, service);

        Func<Task> act = () => handler.Handle(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.ErrorCode.Should().Be("FORBIDDEN");
        subscriptionCalls.Should().Be(0);
        dispatchCalls.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenOperatorValidationIsUnavailable_Preserves503AndDoesNotDispatch()
    {
        var subscriptionCalls = 0;
        var dispatchCalls = 0;
        var identityClient = TestProxy<IIdentityInternalClient>.Create((method, _) =>
        {
            if (method.Name == nameof(IIdentityInternalClient.ValidateOperatorSubscriptionCanWriteAsync))
            {
                subscriptionCalls++;
                return OperatorWriteEligibilityValidation.Allowed();
            }

            return new OperatorWriteEligibilityValidation(
                false,
                503,
                "UPSTREAM_UNAVAILABLE",
                "Identity is unavailable.");
        });
        var service = TestProxy<IShuttleDispatchService>.Create((method, _) =>
        {
            if (method.Name == nameof(IShuttleDispatchService.CreateAsync))
                dispatchCalls++;

            return null;
        });
        var handler = new CreateShuttleTripCommandHandler(identityClient, service);

        Func<Task> act = () => handler.Handle(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TripSubscriptionWriteBlockedException>();
        exception.Which.StatusCode.Should().Be(503);
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        subscriptionCalls.Should().Be(0);
        dispatchCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(402, "SUBSCRIPTION_EXPIRED")]
    [InlineData(403, "SUBSCRIPTION_MODULE_DISABLED")]
    [InlineData(503, "UPSTREAM_UNAVAILABLE")]
    public async Task Handle_WhenSubscriptionBlocksWrite_PreservesStatusAndDoesNotDispatch(
        int statusCode,
        string errorCode)
    {
        var dispatchCalls = 0;
        var identityClient = TestProxy<IIdentityInternalClient>.Create((method, _) =>
            method.Name == nameof(IIdentityInternalClient.ValidateOperatorSubscriptionCanWriteAsync)
                ? new OperatorWriteEligibilityValidation(false, statusCode, errorCode, "Subscription write blocked.")
                : OperatorWriteEligibilityValidation.Allowed());
        var service = TestProxy<IShuttleDispatchService>.Create((method, _) =>
        {
            if (method.Name == nameof(IShuttleDispatchService.CreateAsync))
                dispatchCalls++;

            return null;
        });
        var handler = new CreateShuttleTripCommandHandler(identityClient, service);

        Func<Task> act = () => handler.Handle(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TripSubscriptionWriteBlockedException>();
        exception.Which.StatusCode.Should().Be(statusCode);
        exception.Which.ErrorCode.Should().Be(errorCode);
        dispatchCalls.Should().Be(0);
    }

    private static CreateShuttleTripCommand CreateCommand()
    {
        var departure = DateTimeOffset.UtcNow.AddHours(1);
        return new CreateShuttleTripCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            departure,
            departure.AddMinutes(30),
            [Guid.NewGuid()],
            "Morning shuttle");
    }
}
