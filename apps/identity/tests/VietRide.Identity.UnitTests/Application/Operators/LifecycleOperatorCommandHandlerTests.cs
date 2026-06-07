using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.ApproveOperator;
using VietRide.Identity.Application.Features.Admin.RejectOperator;
using VietRide.Identity.Application.Features.Admin.SuspendOperator;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Operators;

public sealed class LifecycleOperatorCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 7, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid CallerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Approve_HappyPath_ApprovesOperatorActivatesTrialAndWritesActivityLog()
    {
        var fixture = new Fixture();
        var operatorEntity = PendingOperator();
        var subscription = OperatorSubscription.CreatePendingApproval(operatorEntity.Id, SubscriptionPlan.StarterPlanId, FixedNow.AddDays(-1));
        fixture.Operators.GetByIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(operatorEntity);
        fixture.OperatorSubscriptions.GetCurrentByOperatorIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(subscription);

        var response = await fixture.ApproveHandler.Handle(
            new ApproveOperatorCommand(UserRole.SYSTEM_ADMIN.ToString(), CallerUserId, operatorEntity.Id),
            CancellationToken.None);

        response.OperatorId.Should().Be(operatorEntity.Id);
        response.RegistrationStatus.Should().Be(OperatorRegistrationStatus.APPROVED.ToString());
        operatorEntity.ApprovedByUserId.Should().Be(CallerUserId);
        operatorEntity.ApprovedAt.Should().Be(FixedNow);
        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        subscription.StartedAt.Should().Be(FixedNow);
        subscription.ExpiresAt.Should().Be(FixedNow.AddDays(30));
        await fixture.ActivityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(x => x.Action == ActivityLogAction.APPROVE_OPERATOR && HasLifecycleMetadata(x.Metadata, operatorEntity.Id, "SYSTEM_ADMIN_APPROVE_OPERATOR")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_InvalidState_ThrowsValidationExceptionWithoutActivityLog()
    {
        var fixture = new Fixture();
        var operatorEntity = PendingOperator();
        operatorEntity.Approve(CallerUserId, FixedNow.AddDays(-1));
        var subscription = OperatorSubscription.CreateActiveTrial(operatorEntity.Id, SubscriptionPlan.StarterPlanId, FixedNow.AddDays(-1), FixedNow.AddDays(29));
        fixture.Operators.GetByIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(operatorEntity);
        fixture.OperatorSubscriptions.GetCurrentByOperatorIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(subscription);

        var act = () => fixture.ApproveHandler.Handle(
            new ApproveOperatorCommand(UserRole.SYSTEM_ADMIN.ToString(), CallerUserId, operatorEntity.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        await fixture.ActivityLogs.DidNotReceive().AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_HappyPath_RejectsOperatorCancelsPendingApprovalSubscriptionAndWritesActivityLog()
    {
        var fixture = new Fixture();
        var operatorEntity = PendingOperator();
        var subscription = OperatorSubscription.CreatePendingApproval(operatorEntity.Id, SubscriptionPlan.StarterPlanId, FixedNow.AddDays(-1));
        fixture.Operators.GetByIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(operatorEntity);
        fixture.OperatorSubscriptions.GetCurrentByOperatorIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(subscription);

        var response = await fixture.RejectHandler.Handle(
            new RejectOperatorCommand(UserRole.SYSTEM_ADMIN.ToString(), CallerUserId, operatorEntity.Id, "Business documents are invalid."),
            CancellationToken.None);

        response.OperatorId.Should().Be(operatorEntity.Id);
        response.RegistrationStatus.Should().Be(OperatorRegistrationStatus.REJECTED.ToString());
        operatorEntity.RejectedByUserId.Should().Be(CallerUserId);
        operatorEntity.RejectedAt.Should().Be(FixedNow);
        operatorEntity.RejectReason.Should().Be("Business documents are invalid.");
        subscription.Status.Should().Be(SubscriptionStatus.CANCELLED);
        await fixture.ActivityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(x => x.Action == ActivityLogAction.REJECT_OPERATOR && HasLifecycleMetadata(x.Metadata, operatorEntity.Id, "SYSTEM_ADMIN_REJECT_OPERATOR")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_InvalidState_ThrowsValidationExceptionWithoutCancellingSubscriptionOrActivityLog()
    {
        var fixture = new Fixture();
        var operatorEntity = PendingOperator();
        operatorEntity.Approve(CallerUserId, FixedNow.AddDays(-1));
        var subscription = OperatorSubscription.CreateActiveTrial(operatorEntity.Id, SubscriptionPlan.StarterPlanId, FixedNow.AddDays(-1), FixedNow.AddDays(29));
        fixture.Operators.GetByIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(operatorEntity);
        fixture.OperatorSubscriptions.GetCurrentByOperatorIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(subscription);

        var act = () => fixture.RejectHandler.Handle(
            new RejectOperatorCommand(UserRole.SYSTEM_ADMIN.ToString(), CallerUserId, operatorEntity.Id, "Invalid documents."),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        await fixture.ActivityLogs.DidNotReceive().AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_HappyPath_SuspendsApprovedOperatorWithoutActivityLog()
    {
        var fixture = new Fixture();
        var operatorEntity = PendingOperator();
        operatorEntity.Approve(CallerUserId, FixedNow.AddDays(-1));
        fixture.Operators.GetByIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(operatorEntity);

        var response = await fixture.SuspendHandler.Handle(
            new SuspendOperatorCommand(UserRole.SYSTEM_ADMIN.ToString(), CallerUserId, operatorEntity.Id, "Policy violation"),
            CancellationToken.None);

        response.OperatorId.Should().Be(operatorEntity.Id);
        response.RegistrationStatus.Should().Be(OperatorRegistrationStatus.SUSPENDED.ToString());
        operatorEntity.SuspendedAt.Should().Be(FixedNow);
        operatorEntity.SuspendReason.Should().Be("Policy violation");
        await fixture.ActivityLogs.DidNotReceive().AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_InvalidState_ThrowsValidationExceptionWithoutActivityLog()
    {
        var fixture = new Fixture();
        var operatorEntity = PendingOperator();
        fixture.Operators.GetByIdAsync(operatorEntity.Id, Arg.Any<CancellationToken>()).Returns(operatorEntity);

        var act = () => fixture.SuspendHandler.Handle(
            new SuspendOperatorCommand(UserRole.SYSTEM_ADMIN.ToString(), CallerUserId, operatorEntity.Id, "Policy violation"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        operatorEntity.RegistrationStatus.Should().Be(OperatorRegistrationStatus.PENDING);
        await fixture.ActivityLogs.DidNotReceive().AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    private static Operator PendingOperator()
        => Operator.CreatePending(
            "Operator Co",
            $"BRN-{Guid.NewGuid():N}",
            $"TAX-{Guid.NewGuid():N}",
            $"operator-{Guid.NewGuid():N}@example.com",
            "+84901234567",
            "1 Street",
            "Ward",
            "District",
            "Province",
            "Operator Admin",
            "+84901234568");

    private static bool HasLifecycleMetadata(string? metadata, Guid operatorId, string source)
    {
        using var document = JsonDocument.Parse(metadata!);
        var root = document.RootElement;

        return root.TryGetProperty("operatorId", out var operatorIdElement)
            && operatorIdElement.GetGuid() == operatorId
            && root.TryGetProperty("actorUserId", out var actorUserId)
            && actorUserId.GetGuid() == CallerUserId
            && root.TryGetProperty("source", out var sourceElement)
            && sourceElement.GetString() == source;
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Clock.UtcNow.Returns(FixedNow);
            ActivityLogs.AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<ActivityLog>()));

            ApproveHandler = new ApproveOperatorCommandHandler(Operators, OperatorSubscriptions, ActivityLogs, Clock);
            RejectHandler = new RejectOperatorCommandHandler(Operators, OperatorSubscriptions, ActivityLogs, Clock);
            SuspendHandler = new SuspendOperatorCommandHandler(Operators, Clock);
        }

        public IOperatorRepository Operators { get; } = Substitute.For<IOperatorRepository>();
        public IOperatorSubscriptionRepository OperatorSubscriptions { get; } = Substitute.For<IOperatorSubscriptionRepository>();
        public IActivityLogRepository ActivityLogs { get; } = Substitute.For<IActivityLogRepository>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public ApproveOperatorCommandHandler ApproveHandler { get; }
        public RejectOperatorCommandHandler RejectHandler { get; }
        public SuspendOperatorCommandHandler SuspendHandler { get; }
    }
}
