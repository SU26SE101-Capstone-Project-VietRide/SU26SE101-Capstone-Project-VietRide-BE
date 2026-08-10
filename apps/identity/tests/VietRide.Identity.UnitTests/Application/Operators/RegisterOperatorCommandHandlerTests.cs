using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Operators.RegisterOperator;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Operators;

public sealed class RegisterOperatorCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 7, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_HappyPath_CreatesPendingOperatorAdminAndSubscriptionUsage()
    {
        var fixture = new Fixture();
        var command = ValidCommand();
        OperatorSubscription? capturedSubscription = null;
        var capturedEvents = new List<(string EventType, string Payload)>();
        var capturedCanonicalEvents = new List<(Guid EventId, string EventType, string Payload)>();

        fixture.SubscriptionPlans.GetStarterPlanAsync(Arg.Any<CancellationToken>())
            .Returns(SubscriptionPlan.CreateStarter());
        fixture.OperatorSubscriptions
            .AddAsync(Arg.Do<OperatorSubscription>(x => capturedSubscription = x), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<OperatorSubscription>()));
        fixture.Outbox.EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedEvents.Add((ci.ArgAt<string>(0), ci.ArgAt<string>(1)));
                return Task.CompletedTask;
            });
        fixture.Outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedCanonicalEvents.Add((
                    ci.ArgAt<Guid>(0),
                    ci.ArgAt<string>(1),
                    ci.ArgAt<string>(2)));
                return Task.CompletedTask;
            });

        var response = await fixture.Handler.Handle(command, CancellationToken.None);

        response.OperatorId.Should().NotBeEmpty();
        response.Message.Should().Be("Đơn đăng ký đã nhận, vui lòng xác thực email");
        capturedSubscription.Should().NotBeNull();
        capturedSubscription!.Status.Should().Be(SubscriptionStatus.PENDING_APPROVAL);
        capturedSubscription.CurrentOperatorUsers.Should().Be(1);

        // OTP delivery is now via Outbox (identity.otp.requested), not direct email.
        var otpEntry = capturedEvents.Should().Contain(e => e.EventType == "identity.otp.requested").Which;
        using var otpDoc = JsonDocument.Parse(otpEntry.Payload);
        var otpRoot = otpDoc.RootElement;
        otpRoot.GetProperty("email").GetString().Should().Be("operator@example.com");
        otpRoot.GetProperty("purpose").GetString().Should().Be("REGISTRATION");
        otpRoot.GetProperty("ttlMinutes").GetInt32().Should().Be(10);
        otpRoot.GetProperty("code").GetString().Should().HaveLength(6);

        var registrationEntry = capturedCanonicalEvents.Should()
            .ContainSingle(e => e.EventType == "identity.operator.registration_submitted")
            .Which;
        using var registrationDoc = JsonDocument.Parse(registrationEntry.Payload);
        var registrationRoot = registrationDoc.RootElement;
        registrationRoot.GetProperty("eventId").GetGuid().Should().Be(registrationEntry.EventId);
        registrationRoot.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(FixedNow);
        registrationRoot.GetProperty("operatorId").GetGuid().Should().Be(response.OperatorId);
        registrationRoot.GetProperty("companyName").GetString().Should().Be("Operator Co");

        await fixture.ActivityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(x => x.Action == ActivityLogAction.CREATE_OPERATOR && HasCanonicalSelfRegisterMetadata(x.Metadata)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LocalRepresentativePhone_NormalizesBeforeDuplicateLookup()
    {
        var fixture = new Fixture();
        var command = ValidCommand() with { RepresentativePhone = "0901234568" };
        fixture.SubscriptionPlans.GetStarterPlanAsync(Arg.Any<CancellationToken>())
            .Returns(SubscriptionPlan.CreateStarter());

        await fixture.Handler.Handle(command, CancellationToken.None);

        await fixture.Users.Received(1).GetByPhoneAsync("+84901234568", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidContactPhone_ThrowsValidationException()
    {
        var fixture = new Fixture();
        var command = ValidCommand() with { ContactPhone = "not-a-phone" };

        var act = () => fixture.Handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(x => x.Errors.Any(error => error.Field == nameof(RegisterOperatorCommand.ContactPhone)));
        await fixture.Users.DidNotReceive().GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OtpCollision_RetriesBeforeEnqueuingOtpEvent()
    {
        var fixture = new Fixture();
        fixture.SubscriptionPlans.GetStarterPlanAsync(Arg.Any<CancellationToken>())
            .Returns(SubscriptionPlan.CreateStarter());
        fixture.Tokens.TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>())
            .Returns(false, true);

        await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        await fixture.Tokens.Received(2).TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        // OTP is now delivered via outbox — verify exactly one identity.otp.requested was enqueued.
        await fixture.Outbox.Received(1).EnqueueAsync(
            "identity.otp.requested",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateBusinessRegistration_ThrowsConflict()
    {
        var fixture = new Fixture();
        var existing = Operator.CreatePending(
            "Existing Operator",
            "BRN-001",
            "TAX-OLD",
            "existing@example.com",
            "+84901234000",
            "Street",
            "Ward",
            "Province",
            "Rep",
            "+84901234001");
        fixture.Operators.GetByBusinessRegistrationNumberAsync("BRN-001", Arg.Any<CancellationToken>())
            .Returns(existing);

        var act = () => fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(x => x.ErrorCode == "OPERATOR_DUPLICATE_REGISTRATION");
        await fixture.Users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    private static bool HasCanonicalSelfRegisterMetadata(string? metadata)
    {
        using var document = JsonDocument.Parse(metadata!);
        var root = document.RootElement;

        return root.TryGetProperty("operatorId", out _)
            && root.TryGetProperty("actorUserId", out _)
            && root.TryGetProperty("source", out var source)
            && source.GetString() == "SELF_REGISTER"
            && !root.TryGetProperty("operatorAdminUserId", out _);
    }

    private static RegisterOperatorCommand ValidCommand()
        => new(
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
            Password: "Password123!");

    private sealed class Fixture
    {
        public Fixture()
        {
            PasswordHasher.Hash(Arg.Any<string>()).Returns("$2a$12$abcdefghijklmnopqrstuuabcdefghijklmnopqrstuuabcdefghijkl");
            Clock.UtcNow.Returns(FixedNow);
            Users.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<User>()));
            Operators.AddAsync(Arg.Any<Operator>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<Operator>()));
            Tokens.AddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<EmailVerificationToken>()));
            Tokens.TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>())
                .Returns(true);
            ActivityLogs.AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(call.Arg<ActivityLog>()));

            Handler = new RegisterOperatorCommandHandler(
                Operators,
                Users,
                OperatorSubscriptions,
                SubscriptionPlans,
                Tokens,
                ActivityLogs,
                PasswordHasher,
                Outbox,
                Clock,
                NullLogger<RegisterOperatorCommandHandler>.Instance);
        }

        public IOperatorRepository Operators { get; } = Substitute.For<IOperatorRepository>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IOperatorSubscriptionRepository OperatorSubscriptions { get; } = Substitute.For<IOperatorSubscriptionRepository>();
        public ISubscriptionPlanRepository SubscriptionPlans { get; } = Substitute.For<ISubscriptionPlanRepository>();
        public IEmailVerificationTokenRepository Tokens { get; } = Substitute.For<IEmailVerificationTokenRepository>();
        public IActivityLogRepository ActivityLogs { get; } = Substitute.For<IActivityLogRepository>();
        public IPasswordHasher PasswordHasher { get; } = Substitute.For<IPasswordHasher>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public RegisterOperatorCommandHandler Handler { get; }
    }
}
