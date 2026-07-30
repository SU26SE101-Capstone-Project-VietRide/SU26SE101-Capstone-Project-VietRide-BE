using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.Deliver;
using VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;
using VietRide.Parcel.Application.Features.Parcels.ResendDeliveryEmail;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class Day31DeliveryIssuanceTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid RecipientUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public void DeliveryTokenHasher_NormalizesUuidDAndReturnsLowercaseSha256()
    {
        var token = Guid.Parse("11111111-2222-4333-8444-555555555555");

        DeliveryTokenHasher.Hash(token).Should().Be(
            "cf4c4732fd3b8f8a55b60871950a2f22c893ea7afd75d2146826534e3f67cc49");
    }

    [Fact]
    public async Task Deliver_WithRecipientEmail_PersistsOnlyHashAndEmailsBeforeCommit()
    {
        var parcel = CreateParcel(ParcelStatus.UNLOADED);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var outbox = CreateOutbox();
        var unitOfWork = CreateUnitOfWork();
        var order = new List<string>();
        ParcelDeliveryToken? storedToken = null;
        ParcelDeliveryEmailRequest? emailRequest = null;
        Guid outboxEventId = Guid.Empty;
        string? outboxPayload = null;

        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkDeliveredPendingConfirmAsync(
                ParcelId,
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));
        tokenRepository.AddAsync(
                Arg.Do<ParcelDeliveryToken>(token => storedToken = token),
                Arg.Any<CancellationToken>())
            .Returns(call => (ParcelDeliveryToken)call[0]);
        emailClient.SendDeliveryLinkAsync(
                Arg.Do<ParcelDeliveryEmailRequest>(request => emailRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("email");
                return Task.CompletedTask;
            });
        outbox.EnqueueAsync(
                Arg.Do<Guid>(eventId => outboxEventId = eventId),
                ParcelOutboxEvents.DeliveredPendingConfirm,
                Arg.Do<string>(payload => outboxPayload = payload),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("save");
                return 1;
            });
        unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("commit");
                return Task.CompletedTask;
            });

        var handler = new DeliverParcelCommandHandler(
            repository,
            tokenRepository,
            AuthorizedAssistantTripClient(),
            emailClient,
            outbox,
            unitOfWork);

        var result = await handler.Handle(
            new DeliverParcelCommand(ParcelId, ActorUserId, OperatorId, null),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.DELIVERED_PENDING_CONFIRM.ToString());
        order.Should().Equal("email", "save", "commit");
        storedToken.Should().NotBeNull();
        emailRequest.Should().NotBeNull();
        storedToken!.IssueReason.Should().Be(ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY);
        storedToken.IssuedByUserId.Should().Be(ActorUserId);
        storedToken.TokenHash.Should().Be(DeliveryTokenHasher.Hash(emailRequest!.DeliveryToken));
        storedToken.TokenHash.Should().NotContain(emailRequest.DeliveryToken.ToString("D"));
        storedToken.Id.Should().Be(emailRequest.DeliveryTokenId);
        storedToken.ExpiresAt.Should().Be(emailRequest.ExpiresAt);
        emailRequest.ToEmail.Should().Be("recipient@example.com");

        outboxEventId.Should().NotBeEmpty();
        using var payloadDocument = JsonDocument.Parse(outboxPayload!);
        var payload = payloadDocument.RootElement;
        payload.GetProperty("eventId").GetGuid().Should().Be(outboxEventId);
        payload.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
        payload.GetProperty("expiresAt").GetDateTimeOffset().Should().Be(storedToken.ExpiresAt);
        payload.TryGetProperty("deliveryToken", out _).Should().BeFalse();
        payload.TryGetProperty("deliveryUrl", out _).Should().BeFalse();
        payload.TryGetProperty("recipientEmail", out _).Should().BeFalse();
        outboxPayload.Should().NotContain(emailRequest.DeliveryToken.ToString("D"));
    }

    [Fact]
    public async Task Deliver_WithoutRecipientEmail_CommitsWithoutTokenEmailOrExpiry()
    {
        var parcel = CreateParcel(ParcelStatus.UNLOADED, recipientEmail: null);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var outbox = CreateOutbox();
        var unitOfWork = CreateUnitOfWork();
        string? outboxPayload = null;

        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkDeliveredPendingConfirmAsync(
                ParcelId,
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));
        outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                ParcelOutboxEvents.DeliveredPendingConfirm,
                Arg.Do<string>(payload => outboxPayload = payload),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = new DeliverParcelCommandHandler(
            repository,
            tokenRepository,
            AuthorizedAssistantTripClient(),
            emailClient,
            outbox,
            unitOfWork);

        await handler.Handle(
            new DeliverParcelCommand(ParcelId, ActorUserId, OperatorId, null),
            CancellationToken.None);

        await tokenRepository.DidNotReceive().AddAsync(
            Arg.Any<ParcelDeliveryToken>(),
            Arg.Any<CancellationToken>());
        await emailClient.DidNotReceive().SendDeliveryLinkAsync(
            Arg.Any<ParcelDeliveryEmailRequest>(),
            Arg.Any<CancellationToken>());
        using var payloadDocument = JsonDocument.Parse(outboxPayload!);
        payloadDocument.RootElement.TryGetProperty("expiresAt", out _).Should().BeFalse();
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_WhenNotificationRejects_RollsBackWithoutSavingOrOutbox()
    {
        var parcel = CreateParcel(ParcelStatus.UNLOADED);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var outbox = CreateOutbox();
        var unitOfWork = CreateUnitOfWork();

        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryMarkDeliveredPendingConfirmAsync(
                ParcelId,
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));
        tokenRepository.AddAsync(
                Arg.Any<ParcelDeliveryToken>(),
                Arg.Any<CancellationToken>())
            .Returns(call => (ParcelDeliveryToken)call[0]);
        emailClient.SendDeliveryLinkAsync(
                Arg.Any<ParcelDeliveryEmailRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Notification rejected the request.")));

        var handler = new DeliverParcelCommandHandler(
            repository,
            tokenRepository,
            AuthorizedAssistantTripClient(),
            emailClient,
            outbox,
            unitOfWork);

        var action = () => handler.Handle(
            new DeliverParcelCommand(ParcelId, ActorUserId, OperatorId, null),
            CancellationToken.None);

        await action.Should().ThrowAsync<ParcelDependencyUnavailableException>()
            .Where(exception => exception.ErrorCode == "UPSTREAM_UNAVAILABLE");
        await unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_PendingConfirmation_RotatesTokenAndReturnsNewExpiry()
    {
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var unitOfWork = CreateUnitOfWork();
        ParcelDeliveryToken? storedToken = null;
        ParcelDeliveryEmailRequest? emailRequest = null;

        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryPrepareDeliveryResendAsync(
                ParcelId,
                ParcelStatus.DELIVERED_PENDING_CONFIRM,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));
        tokenRepository.AddAsync(
                Arg.Do<ParcelDeliveryToken>(token => storedToken = token),
                Arg.Any<CancellationToken>())
            .Returns(call => (ParcelDeliveryToken)call[0]);
        emailClient.SendDeliveryLinkAsync(
                Arg.Do<ParcelDeliveryEmailRequest>(request => emailRequest = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = CreateResendHandler(
            repository,
            tokenRepository,
            AuthorizedCrewTripClient(),
            emailClient,
            CreateOutbox(),
            Substitute.For<IParcelStatsRepository>(),
            unitOfWork);

        var result = await handler.Handle(
            new ResendDeliveryEmailCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "OPERATOR_STAFF"),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.DELIVERED_PENDING_CONFIRM.ToString());
        result.ExpiresAt.Should().Be(emailRequest!.ExpiresAt);
        storedToken.Should().NotBeNull();
        storedToken!.IssueReason.Should().Be(ParcelDeliveryTokenIssueReason.RESEND);
        storedToken.TokenHash.Should().Be(DeliveryTokenHasher.Hash(emailRequest.DeliveryToken));
        await tokenRepository.Received(1).RevokeActiveAsync(
            ParcelId,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_RejectedInsideUndoWindow_RestoresPendingAndDecrementsRejectedStat()
    {
        var parcel = CreateParcel(
            ParcelStatus.DELIVERY_REJECTED,
            rejectedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var outbox = CreateOutbox();
        var stats = Substitute.For<IParcelStatsRepository>();
        var unitOfWork = CreateUnitOfWork();

        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryPrepareDeliveryResendAsync(
                ParcelId,
                ParcelStatus.DELIVERY_REJECTED,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));
        tokenRepository.AddAsync(
                Arg.Any<ParcelDeliveryToken>(),
                Arg.Any<CancellationToken>())
            .Returns(call => (ParcelDeliveryToken)call[0]);
        emailClient.SendDeliveryLinkAsync(
                Arg.Any<ParcelDeliveryEmailRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = CreateResendHandler(
            repository,
            tokenRepository,
            AuthorizedCrewTripClient(),
            emailClient,
            outbox,
            stats,
            unitOfWork);

        var result = await handler.Handle(
            new ResendDeliveryEmailCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "OPERATOR_ADMIN"),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.DELIVERED_PENDING_CONFIRM.ToString());
        await outbox.Received(1).EnqueueAsync(
            ParcelOutboxEvents.DeliveryRejectUndone,
            Arg.Is<string>(payload => payload.Contains(ParcelId.ToString(), StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
        await stats.Received(1).UpsertIncrementAsync(
            OperatorId,
            Arg.Any<DateOnly>(),
            0, 0, 0, -1, 0, 0, 0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_RejectedAfterUndoWindow_ReturnsErrorWithoutStartingTransaction()
    {
        var parcel = CreateParcel(
            ParcelStatus.DELIVERY_REJECTED,
            rejectedAt: DateTimeOffset.UtcNow.AddMinutes(-16));
        var repository = Substitute.For<IParcelRepository>();
        var unitOfWork = CreateUnitOfWork();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = CreateResendHandler(
            repository,
            Substitute.For<IParcelDeliveryTokenRepository>(),
            AuthorizedCrewTripClient(),
            Substitute.For<IParcelDeliveryEmailClient>(),
            CreateOutbox(),
            Substitute.For<IParcelStatsRepository>(),
            unitOfWork);

        var action = () => handler.Handle(
            new ResendDeliveryEmailCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "OPERATOR_STAFF"),
            CancellationToken.None);

        await action.Should().ThrowAsync<BadRequestException>()
            .Where(exception =>
                exception.ErrorCode == "PARCEL_DELIVERY_REJECTED_WINDOW_EXPIRED");
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WhenNotificationRejects_RollsBackRotation()
    {
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var unitOfWork = CreateUnitOfWork();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryPrepareDeliveryResendAsync(
                ParcelId,
                ParcelStatus.DELIVERED_PENDING_CONFIRM,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERED_PENDING_CONFIRM));
        tokenRepository.AddAsync(
                Arg.Any<ParcelDeliveryToken>(),
                Arg.Any<CancellationToken>())
            .Returns(call => (ParcelDeliveryToken)call[0]);
        emailClient.SendDeliveryLinkAsync(
                Arg.Any<ParcelDeliveryEmailRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Notification rejected the request.")));

        var handler = CreateResendHandler(
            repository,
            tokenRepository,
            AuthorizedCrewTripClient(),
            emailClient,
            CreateOutbox(),
            Substitute.For<IParcelStatsRepository>(),
            unitOfWork);

        var action = () => handler.Handle(
            new ResendDeliveryEmailCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "OPERATOR_STAFF"),
            CancellationToken.None);

        await action.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        await unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WhenActiveTokenSnapshotLosesRace_ReturnsResourceConflict()
    {
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var unitOfWork = CreateUnitOfWork();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryPrepareDeliveryResendAsync(
                ParcelId,
                ParcelStatus.DELIVERED_PENDING_CONFIRM,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);

        var handler = CreateResendHandler(
            repository,
            tokenRepository,
            AuthorizedCrewTripClient(),
            emailClient,
            CreateOutbox(),
            Substitute.For<IParcelStatsRepository>(),
            unitOfWork);

        var action = () => handler.Handle(
            new ResendDeliveryEmailCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "OPERATOR_STAFF"),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedConflictException>()
            .Where(exception => exception.ErrorCode == "RESOURCE_CONFLICT");
        await emailClient.DidNotReceive().SendDeliveryLinkAsync(
            Arg.Any<ParcelDeliveryEmailRequest>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WithoutRecipientEmail_ReturnsCanonical422()
    {
        var parcel = CreateParcel(
            ParcelStatus.DELIVERED_PENDING_CONFIRM,
            recipientEmail: null);
        var repository = Substitute.For<IParcelRepository>();
        var emailClient = Substitute.For<IParcelDeliveryEmailClient>();
        var unitOfWork = CreateUnitOfWork();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = CreateResendHandler(
            repository,
            Substitute.For<IParcelDeliveryTokenRepository>(),
            AuthorizedCrewTripClient(),
            emailClient,
            CreateOutbox(),
            Substitute.For<IParcelStatsRepository>(),
            unitOfWork);

        var action = () => handler.Handle(
            new ResendDeliveryEmailCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "OPERATOR_STAFF"),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "PARCEL_RECIPIENT_EMAIL_REQUIRED");
        await emailClient.DidNotReceive().SendDeliveryLinkAsync(
            Arg.Any<ParcelDeliveryEmailRequest>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualConfirm_AssignedDriver_TrimsNoteAndRevokesActiveToken()
    {
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var tripClient = AuthorizedCrewTripClient();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryManualConfirmDeliveryAsync(
                ParcelId,
                OperatorId,
                ActorUserId,
                "confirmed by phone",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.DELIVERY_CONFIRMED));

        var handler = new ManualConfirmDeliveryCommandHandler(
            repository,
            tokenRepository,
            tripClient,
            CreateOutbox(),
            Substitute.For<IParcelStatsRepository>());

        var result = await handler.Handle(
            new ManualConfirmDeliveryCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "  confirmed by phone  ",
                "DRIVER"),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED.ToString());
        await tripClient.Received(1).AuthorizeCrewForTripAsync(
            TripId,
            ActorUserId,
            OperatorId,
            "DRIVER",
            Arg.Any<CancellationToken>());
        await tokenRepository.Received(1).RevokeActiveAsync(
            ParcelId,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualConfirm_UnassignedCrew_ReturnsForbiddenBeforeTransition()
    {
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        tripClient.AuthorizeCrewForTripAsync(
                TripId,
                ActorUserId,
                OperatorId,
                "ASSISTANT",
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Denied));

        var handler = new ManualConfirmDeliveryCommandHandler(
            repository,
            Substitute.For<IParcelDeliveryTokenRepository>(),
            tripClient,
            CreateOutbox(),
            Substitute.For<IParcelStatsRepository>());

        var action = () => handler.Handle(
            new ManualConfirmDeliveryCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "confirmed",
                "ASSISTANT"),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        await repository.DidNotReceive().TryManualConfirmDeliveryAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualConfirm_SameActorAndNormalizedNote_ReplaysWithoutSideEffects()
    {
        var confirmedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var parcel = CreateParcel(ParcelStatus.DELIVERY_CONFIRMED);
        Set(parcel, nameof(parcel.ConfirmedAt), (DateTimeOffset?)confirmedAt);
        Set(parcel, nameof(parcel.ConfirmedByUserId), (Guid?)ActorUserId);
        Set(parcel, nameof(parcel.ConfirmNote), "confirmed by phone");
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var outbox = CreateOutbox();
        var stats = Substitute.For<IParcelStatsRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var handler = new ManualConfirmDeliveryCommandHandler(
            repository,
            tokenRepository,
            AuthorizedCrewTripClient(),
            outbox,
            stats);

        var result = await handler.Handle(
            new ManualConfirmDeliveryCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "  confirmed by phone  ",
                "OPERATOR_STAFF"),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED.ToString());
        result.ConfirmedAt.Should().Be(confirmedAt);
        await repository.DidNotReceive().TryManualConfirmDeliveryAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await tokenRepository.DidNotReceive().RevokeActiveAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await stats.DidNotReceive().UpsertIncrementAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateOnly>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualConfirm_ConcurrentSameRequest_ReplaysWithoutDuplicateSideEffects()
    {
        var confirmedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var parcel = CreateParcel(ParcelStatus.DELIVERED_PENDING_CONFIRM);
        var repository = Substitute.For<IParcelRepository>();
        var tokenRepository = Substitute.For<IParcelDeliveryTokenRepository>();
        var outbox = CreateOutbox();
        var stats = Substitute.For<IParcelStatsRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryManualConfirmDeliveryAsync(
                ParcelId,
                OperatorId,
                ActorUserId,
                "confirmed by phone",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);
        repository.GetManualConfirmationSnapshotAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(new ParcelManualConfirmationSnapshot(
                ParcelId,
                ParcelStatus.DELIVERY_CONFIRMED,
                confirmedAt,
                ActorUserId,
                "confirmed by phone"));

        var handler = new ManualConfirmDeliveryCommandHandler(
            repository,
            tokenRepository,
            AuthorizedCrewTripClient(),
            outbox,
            stats);

        var result = await handler.Handle(
            new ManualConfirmDeliveryCommand(
                ParcelId,
                ActorUserId,
                OperatorId,
                "  confirmed by phone  ",
                "OPERATOR_STAFF"),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED.ToString());
        result.ConfirmedAt.Should().Be(confirmedAt);
        await tokenRepository.DidNotReceive().RevokeActiveAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await stats.DidNotReceive().UpsertIncrementAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateOnly>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    private static ResendDeliveryEmailCommandHandler CreateResendHandler(
        IParcelRepository repository,
        IParcelDeliveryTokenRepository tokenRepository,
        ITripServiceClient tripClient,
        IParcelDeliveryEmailClient emailClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository stats,
        IUnitOfWork unitOfWork)
    {
        tokenRepository.FindActiveByParcelIdAsync(
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(CreateActiveDeliveryToken());

        return new(
            repository,
            tokenRepository,
            tripClient,
            emailClient,
            outbox,
            stats,
            unitOfWork);
    }

    private static ITripServiceClient AuthorizedAssistantTripClient()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeAssistantForTripAsync(
                TripId,
                ActorUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(
                TripCrewAuthorizationOutcomeKind.Authorized));
        return tripClient;
    }

    private static ITripServiceClient AuthorizedCrewTripClient()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.AuthorizeCrewForTripAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(
                TripCrewAuthorizationOutcomeKind.Authorized));
        return tripClient;
    }

    private static IIntegrationEventOutbox CreateOutbox()
    {
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        outbox.EnqueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return outbox;
    }

    private static IUnitOfWork CreateUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(1);
        unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        unitOfWork.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return unitOfWork;
    }

    private static ParcelEntity CreateParcel(
        ParcelStatus status,
        string? recipientEmail = "recipient@example.com",
        DateTimeOffset? rejectedAt = null)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-DAY31-001",
            SenderUserId,
            RecipientUserId,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            recipientEmail,
            OperatorId,
            TripId,
            null,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

        Set(parcel, nameof(parcel.Id), ParcelId);
        Set(parcel, nameof(parcel.Status), status);
        Set(parcel, nameof(parcel.RejectedAt), rejectedAt);
        return parcel;
    }

    private static ParcelPaymentTransitionSnapshot Snapshot(ParcelStatus status)
        => new(
            ParcelId,
            "VRP-DAY31-001",
            status,
            100_000,
            0,
            OperatorId,
            TripId,
            null,
            SenderUserId,
            ParcelSizeCategory.MEDIUM,
            null);

    private static ParcelDeliveryToken CreateActiveDeliveryToken()
    {
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        return ParcelDeliveryToken.Issue(
            ParcelId,
            DeliveryTokenHasher.Hash(Guid.NewGuid()),
            issuedAt.AddHours(48),
            ActorUserId,
            ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
            issuedAt);
    }

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property!.SetValue(target, value);
    }
}
