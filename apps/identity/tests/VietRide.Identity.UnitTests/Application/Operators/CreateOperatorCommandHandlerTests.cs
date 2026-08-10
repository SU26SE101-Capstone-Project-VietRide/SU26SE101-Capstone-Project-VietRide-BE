using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.CreateOperator;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Operators;

public sealed class CreateOperatorCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 7, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid CallerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Handle_HappyPath_CreatesApprovedOperatorInitialPasswordAndActiveTrial()
    {
        var fixture = new Fixture();
        Operator? capturedOperator = null;
        fixture.SubscriptionPlans.GetStarterPlanAsync(Arg.Any<CancellationToken>())
            .Returns(SubscriptionPlan.CreateStarter());
        fixture.Operators
            .AddAsync(Arg.Do<Operator>(x => capturedOperator = x), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<Operator>()));

        var response = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        response.Operator.RegistrationStatus.Should().Be(OperatorRegistrationStatus.APPROVED.ToString());
        response.Operator.ContactEmail.Should().Be("operator@example.com");
        response.Operator.ContactPhone.Should().Be("+84901234567");
        response.Operator.BusinessRegistrationNumber.Should().Be("BRN-001");
        response.Operator.TaxCode.Should().Be("TAX-001");
        response.AdminUser.Role.Should().Be(UserRole.OPERATOR_ADMIN.ToString());
        response.AdminUser.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD.ToString());
        response.AdminUser.DisplayName.Should().Be("Operator Admin");
        capturedOperator.Should().NotBeNull();
        capturedOperator!.AddressStreet.Should().Be("1 Street");
        capturedOperator.AddressWard.Should().Be("Ward");
        capturedOperator.AddressProvince.Should().Be("Province");
        capturedOperator.RepresentativeName.Should().Be("Operator Admin");
        capturedOperator.RepresentativePhone.Should().Be("+84901234568");
        response.Subscription.Status.Should().Be(SubscriptionStatus.ACTIVE.ToString());
        response.Subscription.StartedAt.Should().Be(FixedNow);
        response.Subscription.ExpiresAt.Should().Be(FixedNow.AddDays(30));
        response.Subscription.CurrentOperatorUsers.Should().Be(1);
        await fixture.EmailService.Received(1).SendAccountCreatedLinkAsync(
            "operator@example.com",
            Arg.Is<AccountCreatedEmailDto>(x => x.ExpiresAt == FixedNow.AddHours(48)),
            Arg.Any<CancellationToken>());
        await fixture.ActivityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(x => x.Action == ActivityLogAction.CREATE_OPERATOR && HasCanonicalAdminCreateMetadata(x.Metadata)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LocalPhones_NormalizesBeforeDuplicateLookupAndPersist()
    {
        var fixture = new Fixture();
        Operator? capturedOperator = null;
        var command = ValidCommand() with { ContactPhone = "0901234567", RepresentativePhone = "0901234568" };
        fixture.SubscriptionPlans.GetStarterPlanAsync(Arg.Any<CancellationToken>())
            .Returns(SubscriptionPlan.CreateStarter());
        fixture.Operators
            .AddAsync(Arg.Do<Operator>(x => capturedOperator = x), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<Operator>()));

        await fixture.Handler.Handle(command, CancellationToken.None);

        await fixture.Users.Received(1).GetByPhoneAsync("+84901234568", Arg.Any<CancellationToken>());
        capturedOperator!.ContactPhone.Should().Be("+84901234567");
        capturedOperator.RepresentativePhone.Should().Be("+84901234568");
    }

    [Fact]
    public async Task Handle_InvalidRepresentativePhone_ThrowsValidationException()
    {
        var fixture = new Fixture();
        var command = ValidCommand() with { RepresentativePhone = "not-a-phone" };

        var act = () => fixture.Handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(x => x.Errors.Any(error => error.Field == nameof(CreateOperatorCommand.RepresentativePhone)));
        await fixture.Users.DidNotReceive().GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithPlanId_RejectsAsValidationError()
    {
        var fixture = new Fixture();
        var command = ValidCommand() with { UnsupportedSubscriptionFields = ["planId"] };

        var act = () => fixture.Handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        await fixture.Operators.DidNotReceive().AddAsync(Arg.Any<Operator>(), Arg.Any<CancellationToken>());
    }

    private static bool HasCanonicalAdminCreateMetadata(string? metadata)
    {
        using var document = JsonDocument.Parse(metadata!);
        var root = document.RootElement;

        return root.TryGetProperty("operatorId", out _)
            && root.TryGetProperty("actorUserId", out var actorUserId)
            && actorUserId.GetGuid() == CallerUserId
            && root.TryGetProperty("targetUserId", out _)
            && root.TryGetProperty("source", out var source)
            && source.GetString() == "SYSTEM_ADMIN_CREATE_OPERATOR"
            && !root.TryGetProperty("operatorAdminUserId", out _)
            && !root.TryGetProperty("callerUserId", out _);
    }

    private static CreateOperatorCommand ValidCommand()
        => new(
            CallerRole: UserRole.SYSTEM_ADMIN.ToString(),
            CallerUserId: CallerUserId,
            Name: "Operator Co",
            ContactEmail: "operator@example.com",
            ContactPhone: "+84901234567",
            BusinessRegistrationNumber: "BRN-001",
            TaxCode: "TAX-001",
            AddressStreet: "1 Street",
            AddressWard: "Ward",
            AddressProvince: "Province",
            RepresentativeName: "Operator Admin",
            RepresentativePhone: "+84901234568",
            UnsupportedSubscriptionFields: []);

    private sealed class Fixture
    {
        public Fixture()
        {
            Clock.UtcNow.Returns(FixedNow);
            InitialPasswordTokens.GenerateCode().Returns("initial-token");
            InitialPasswordTokens.GetExpiresAt(FixedNow).Returns(FixedNow.AddHours(48));
            Users.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<User>()));
            Operators.AddAsync(Arg.Any<Operator>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<Operator>()));
            OperatorSubscriptions.AddAsync(Arg.Any<OperatorSubscription>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<OperatorSubscription>()));
            Tokens.AddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<EmailVerificationToken>()));
            ActivityLogs.AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<ActivityLog>()));

            Handler = new CreateOperatorCommandHandler(
                Operators,
                Users,
                OperatorSubscriptions,
                SubscriptionPlans,
                Tokens,
                ActivityLogs,
                InitialPasswordTokens,
                EmailService,
                Clock);
        }

        public IOperatorRepository Operators { get; } = Substitute.For<IOperatorRepository>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IOperatorSubscriptionRepository OperatorSubscriptions { get; } = Substitute.For<IOperatorSubscriptionRepository>();
        public ISubscriptionPlanRepository SubscriptionPlans { get; } = Substitute.For<ISubscriptionPlanRepository>();
        public IEmailVerificationTokenRepository Tokens { get; } = Substitute.For<IEmailVerificationTokenRepository>();
        public IActivityLogRepository ActivityLogs { get; } = Substitute.For<IActivityLogRepository>();
        public IInitialPasswordTokenService InitialPasswordTokens { get; } = Substitute.For<IInitialPasswordTokenService>();
        public IEmailService EmailService { get; } = Substitute.For<IEmailService>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public CreateOperatorCommandHandler Handler { get; }
    }
}
