using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperator;
using VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;
using VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.UnitTests.Application.Internal.Operators;

public sealed class InternalOperatorHandlersTests
{
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetInternalOperator_ExistingOperator_ReturnsRawLookupDto()
    {
        var operatorEntity = CreateOperator();
        var handler = new GetInternalOperatorQueryHandler(new FakeOperatorRepository(operatorEntity));

        var result = await handler.Handle(new GetInternalOperatorQuery(OperatorId), CancellationToken.None);

        result.OperatorId.Should().Be(OperatorId);
        result.Name.Should().Be("VietRide Limousine");
        result.RegistrationStatus.Should().Be(OperatorRegistrationStatus.APPROVED.ToString());
        result.IsActive.Should().BeTrue();
        result.ContactEmail.Should().Be("ops@example.com");
        result.ContactPhone.Should().Be("+84901234567");
        result.BusinessRegistrationNumber.Should().Be("0312345678");
        result.TaxCode.Should().Be("0312345678");
        result.ParcelNoShowPolicy.Should().BeNull();
        result.CancellationPolicy.Should().NotBeNull();
        result.CancellationPolicy!.Value[0].GetProperty("hoursBeforeDeparture").GetInt32().Should().Be(24);
        result.CancellationPolicy!.Value[0].GetProperty("feePercent").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task GetInternalOperator_WithParcelNoShowPolicy_ReturnsParsedPolicy()
    {
        var operatorEntity = CreateOperator();
        SetProperty(operatorEntity, nameof(Operator.ParcelNoShowPolicy), "{\"noShowFeePercent\":25,\"additionalPaymentTimeoutMinutes\":45}");
        var handler = new GetInternalOperatorQueryHandler(new FakeOperatorRepository(operatorEntity));

        var result = await handler.Handle(new GetInternalOperatorQuery(OperatorId), CancellationToken.None);

        result.ParcelNoShowPolicy.Should().NotBeNull();
        result.ParcelNoShowPolicy!.NoShowFeePercent.Should().Be(25);
        result.ParcelNoShowPolicy.AdditionalPaymentTimeoutMinutes.Should().Be(45);
    }

    [Fact]
    public async Task GetInternalOperator_WithMalformedParcelNoShowPolicy_ReturnsNullPolicy()
    {
        var operatorEntity = CreateOperator();
        SetProperty(operatorEntity, nameof(Operator.ParcelNoShowPolicy), "not-json");
        var handler = new GetInternalOperatorQueryHandler(new FakeOperatorRepository(operatorEntity));

        var result = await handler.Handle(new GetInternalOperatorQuery(OperatorId), CancellationToken.None);

        result.ParcelNoShowPolicy.Should().BeNull();
    }

    [Fact]
    public async Task GetInternalOperator_MissingOperator_ThrowsNotFound()
    {
        var handler = new GetInternalOperatorQueryHandler(new FakeOperatorRepository(null));

        var act = () => handler.Handle(new GetInternalOperatorQuery(OperatorId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetInternalOperatorSubscription_ExistingSubscription_ReturnsPlanLimitsModulesAndUsage()
    {
        var subscription = CreateSubscription(SubscriptionUsageResource.OPERATOR_USERS, 1);
        var plan = SubscriptionPlan.CreateStarter();
        var handler = new GetInternalOperatorSubscriptionQueryHandler(
            new FakeOperatorRepository(CreateOperator()),
            new FakeOperatorSubscriptionRepository((subscription, plan), null),
            CreateClock());

        var result = await handler.Handle(new GetInternalOperatorSubscriptionQuery(OperatorId), CancellationToken.None);

        result.OperatorId.Should().Be(OperatorId);
        result.SubscriptionId.Should().Be(subscription.Id);
        result.Status.Should().Be(SubscriptionStatus.ACTIVE.ToString());
        result.Plan.PlanId.Should().Be(SubscriptionPlan.StarterPlanId);
        result.Plan.Limits.MaxDrivers.Should().Be(5);
        result.Plan.Modules.EnableRag.Should().BeTrue();
        result.Usage.CurrentOperatorUsers.Should().Be(1);
        result.LastResetAt.Should().Be(Now);
    }

    [Fact]
    public async Task IncrementOperatorUsage_WithinLimit_ReturnsUpdatedSubscription()
    {
        var current = CreateSubscription(SubscriptionUsageResource.DRIVERS, 1);
        var updated = CreateSubscription(SubscriptionUsageResource.DRIVERS, 3);
        var plan = SubscriptionPlan.CreateStarter();
        var subscriptions = new FakeOperatorSubscriptionRepository((current, plan), (updated, plan));
        var handler = CreateUsageHandler(subscriptions);

        var result = await handler.Handle(
            new IncrementOperatorUsageCommand(OperatorId, SubscriptionUsageResource.DRIVERS.ToString(), 2),
            CancellationToken.None);

        subscriptions.CapturedResource.Should().Be(SubscriptionUsageResource.DRIVERS);
        subscriptions.CapturedDelta.Should().Be(2);
        result.Usage.CurrentDrivers.Should().Be(3);
    }

    [Fact]
    public async Task IncrementOperatorUsage_Overflow_ThrowsSubscriptionLimitExceeded()
    {
        var current = CreateSubscription(SubscriptionUsageResource.DRIVERS, 5);
        var plan = SubscriptionPlan.CreateStarter();
        var handler = CreateUsageHandler(new FakeOperatorSubscriptionRepository((current, plan), null));

        var act = () => handler.Handle(
            new IncrementOperatorUsageCommand(OperatorId, SubscriptionUsageResource.DRIVERS.ToString(), 1),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<IdentityDomainException>();
        assertion.Which.ErrorCode.Should().Be("SUBSCRIPTION_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task IncrementOperatorUsage_ExpiredSubscription_ThrowsSubscriptionExpired()
    {
        var current = CreateSubscription(SubscriptionUsageResource.DRIVERS, 0);
        current.MarkExpired(Now.AddDays(31));
        var plan = SubscriptionPlan.CreateStarter();
        var handler = CreateUsageHandler(new FakeOperatorSubscriptionRepository((current, plan), null));

        var act = () => handler.Handle(
            new IncrementOperatorUsageCommand(OperatorId, SubscriptionUsageResource.DRIVERS.ToString(), 1),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<IdentityDomainException>();
        assertion.Which.ErrorCode.Should().Be("SUBSCRIPTION_EXPIRED");
    }

    [Fact]
    public async Task IncrementOperatorUsage_CrossesEightyPercent_DelegatesDurableWarningCheck()
    {
        var current = CreateSubscription(SubscriptionUsageResource.DRIVERS, 3);
        var updated = CreateSubscription(SubscriptionUsageResource.DRIVERS, 4);
        var plan = SubscriptionPlan.CreateStarter();
        var usageWarnings = Substitute.For<ISubscriptionUsageWarningPublisher>();
        var handler = new IncrementOperatorUsageCommandHandler(
            new FakeOperatorRepository(CreateOperator()),
            new FakeOperatorSubscriptionRepository((current, plan), (updated, plan)),
            usageWarnings,
            CreateClock());

        await handler.Handle(
            new IncrementOperatorUsageCommand(
                OperatorId,
                SubscriptionUsageResource.DRIVERS.ToString(),
                1),
            CancellationToken.None);

        await usageWarnings.Received(1).EnqueueIfThresholdCrossedAsync(
            updated,
            plan,
            SubscriptionUsageResource.DRIVERS,
            1,
            null,
            Arg.Any<CancellationToken>());
    }

    private static IncrementOperatorUsageCommandHandler CreateUsageHandler(
        IOperatorSubscriptionRepository subscriptions)
    {
        return new IncrementOperatorUsageCommandHandler(
            new FakeOperatorRepository(CreateOperator()),
            subscriptions,
            Substitute.For<ISubscriptionUsageWarningPublisher>(),
            CreateClock());
    }

    private static IClock CreateClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(1));
        return clock;
    }

    private static Operator CreateOperator()
    {
        var operatorEntity = Operator.CreateApproved(
            "VietRide Limousine",
            "0312345678",
            "0312345678",
            "ops@example.com",
            "+84901234567",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Now);
        SetProperty(operatorEntity, nameof(Operator.Id), OperatorId);
        operatorEntity.UpdateProfile(
            "VietRide Limousine",
            "ops@example.com",
            "+84901234567",
            logoUrl: null,
            addressStreet: null,
            addressWard: null,
            addressProvince: null,
            representativeName: null,
            representativePhone: null,
            cancellationPolicy: """[{"hoursBeforeDeparture":24,"feePercent":10}]""",
            parcelNoShowPolicy: null,
            luggagePolicy: null);
        return operatorEntity;
    }

    private static OperatorSubscription CreateSubscription(SubscriptionUsageResource resource, int amount)
    {
        var subscription = OperatorSubscription.CreateActiveTrial(
            OperatorId,
            SubscriptionPlan.StarterPlanId,
            Now,
            Now.AddDays(30));
        SetProperty(subscription, nameof(OperatorSubscription.LastResetAt), Now);
        if (amount > 0)
        {
            subscription.IncrementUsage(resource, amount);
        }

        return subscription;
    }

    private static void SetProperty<T>(object entity, string propertyName, T value)
    {
        var property = entity.GetType().GetProperty(propertyName)!;
        property.SetValue(entity, value);
    }

    private sealed class FakeOperatorRepository : IOperatorRepository
    {
        private readonly Operator? _operator;

        public FakeOperatorRepository(Operator? operatorEntity)
        {
            _operator = operatorEntity;
        }

        public Task<Operator?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_operator);

        public Task<Operator?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_operator);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_operator is not null);

        public Task<Operator?> GetByBusinessRegistrationNumberAsync(
            string businessRegistrationNumber,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Operator?>(null);

        public Task<Operator?> GetByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default)
            => Task.FromResult<Operator?>(null);

        public Task<PagedResult<Operator>> ListAsync(
            QueryOptions options,
            OperatorRegistrationStatus? status,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Operator> AddAsync(Operator entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);

        public void Update(Operator entity) { }
        public void Remove(Operator entity) { }
        public IQueryable<Operator> Query() => Array.Empty<Operator>().AsQueryable();
        public IQueryable<Operator> QueryNoTracking() => Array.Empty<Operator>().AsQueryable();
    }

    private sealed class FakeOperatorSubscriptionRepository : IOperatorSubscriptionRepository
    {
        private readonly (OperatorSubscription Subscription, SubscriptionPlan Plan)? _current;
        private readonly (OperatorSubscription Subscription, SubscriptionPlan Plan)? _incremented;

        public FakeOperatorSubscriptionRepository(
            (OperatorSubscription Subscription, SubscriptionPlan Plan)? current,
            (OperatorSubscription Subscription, SubscriptionPlan Plan)? incremented)
        {
            _current = current;
            _incremented = incremented;
        }

        public SubscriptionUsageResource? CapturedResource { get; private set; }
        public int? CapturedDelta { get; private set; }
        public DateTimeOffset? CapturedDecisionAt { get; private set; }

        public Task<(OperatorSubscription Subscription, SubscriptionPlan Plan)?> GetCurrentWithPlanByOperatorIdAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_current);

        public Task<(OperatorSubscription Subscription, SubscriptionPlan Plan)?> TryIncrementUsageWithinLimitAsync(
            Guid operatorId,
            SubscriptionUsageResource resource,
            int delta,
            DateTimeOffset decisionAt,
            CancellationToken cancellationToken = default)
        {
            CapturedResource = resource;
            CapturedDelta = delta;
            CapturedDecisionAt = decisionAt;
            return Task.FromResult(_incremented);
        }

        public Task<OperatorSubscription?> GetCurrentByOperatorIdAsync(Guid operatorId, CancellationToken cancellationToken = default)
            => Task.FromResult(_current?.Subscription);

        public Task<OperatorSubscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_current?.Subscription);

        public Task<OperatorSubscription> AddAsync(OperatorSubscription entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);

        public void Update(OperatorSubscription entity) { }
        public void Remove(OperatorSubscription entity) { }
        public IQueryable<OperatorSubscription> Query() => Array.Empty<OperatorSubscription>().AsQueryable();
        public IQueryable<OperatorSubscription> QueryNoTracking() => Array.Empty<OperatorSubscription>().AsQueryable();

        public Task<bool> TryCreateOperatorUserWithinLimitAsync(
            Guid operatorId,
            User user,
            EmailVerificationToken initialPasswordToken,
            ActivityLog activityLog,
            UserRole role,
            DateTimeOffset decisionAt,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
