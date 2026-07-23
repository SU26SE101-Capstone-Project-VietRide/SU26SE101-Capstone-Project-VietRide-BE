using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Booking.Application.Features.PendingActions;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.PendingActions;

public sealed class RouteChangeResolverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-23T01:10:00Z");

    [Fact]
    public async Task RouteChangeResolverAcceptsStopOrCancelsWithFullRefund()
    {
        var accepted = new Fixture(100_001);
        await accepted.HandleAsync("ACCEPTED", selectedStopId: accepted.CandidateStopId);

        accepted.Action.ResolvedAction.Should().Be(BookingPendingActionResolved.ACCEPTED);
        accepted.Booking.Status.Should().Be(BookingStatus.CONFIRMED);
        accepted.Booking.PickupStopId.Should().Be(accepted.CandidateStopId);
        accepted.Booking.TotalAmount.Amount.Should().Be(100_001);
        await accepted.Outbox.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(default, default!, default!, default);

        var rejected = new Fixture(100_001);
        string? payload = null;
        rejected.Outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                "booking.booking.cancelled",
                Arg.Do<string>(value => payload = value),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        await rejected.HandleAsync("REJECTED");

        rejected.Action.ResolvedAction.Should().Be(BookingPendingActionResolved.REJECTED);
        rejected.Booking.Status.Should().Be(BookingStatus.CANCELLED);
        rejected.Booking.CancellationReason.Should().Be(BookingCancellationReason.ROUTE_CHANGED_REFUSED);
        rejected.Booking.RefundOverride.Should().BeTrue();
        using var document = JsonDocument.Parse(payload!);
        document.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(100_001);
        document.RootElement.GetProperty("refundOverride").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("cancellationReason").GetString()
            .Should().Be("ROUTE_CHANGED_REFUSED");

        var invalidCandidate = new Fixture(100_001);
        var invalid = () => invalidCandidate.HandleAsync(
            "ACCEPTED",
            selectedStopId: Guid.NewGuid());
        (await invalid.Should().ThrowAsync<CodedValidationException>())
            .Which.ErrorCode.Should().Be("VALIDATION_ERROR");

        var foreignTenant = new Fixture(100_001);
        var foreign = () => foreignTenant.Handler.Handle(new ResolvePendingActionCommand(
            foreignTenant.Booking.Id,
            foreignTenant.Action.Id,
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            "REJECTED",
            null,
            []), CancellationToken.None);
        (await foreign.Should().ThrowAsync<CodedNotFoundException>())
            .Which.ErrorCode.Should().Be("BOOKING_NOT_FOUND");
    }

    [Fact]
    public void DeadlineUsesThirtyMinutesInProgressAndSixtyBeforeProgress()
    {
        CreateRouteChangePendingActionCommandHandler.CalculateDeadline(Now, "IN_PROGRESS")
            .Should().Be(Now.AddMinutes(30));
        CreateRouteChangePendingActionCommandHandler.CalculateDeadline(Now, "SCHEDULED")
            .Should().Be(Now.AddMinutes(60));
        CreateRouteChangePendingActionCommandHandler.CalculateDeadline(Now, "BOARDING")
            .Should().Be(Now.AddMinutes(60));
    }

    private sealed class Fixture
    {
        public Fixture(long totalAmount)
        {
            Booking = CreateConfirmedBooking(totalAmount);
            CandidateStopId = Guid.NewGuid();
            var fallbackDestinationStationId = Guid.NewGuid();
            Action = BookingPendingAction.Create(
                Booking.Id,
                BookingPendingActionReason.ROUTE_CHANGE,
                Now.AddMinutes(20),
                metadata: JsonSerializer.Serialize(new
                {
                    sourceEventId = Guid.NewGuid(),
                    tripId = Booking.TripId,
                    operatorId = Booking.OperatorId,
                    tripStatus = "IN_PROGRESS",
                    alternativeRouteId = Guid.NewGuid(),
                    deadline = Now.AddMinutes(20),
                    originalStopId = Booking.PickupStopId!.Value,
                    fallbackDestinationStationId,
                    shuttleRequired = true,
                    candidateStops = new[]
                    {
                        new
                        {
                            stopId = (Guid?)CandidateStopId,
                            stationId = (Guid?)null,
                            stationName = "Alternative stop",
                            sequence = 1,
                            estimatedArrivalAt = Now.AddMinutes(15),
                        },
                        new
                        {
                            stopId = (Guid?)null,
                            stationId = (Guid?)fallbackDestinationStationId,
                            stationName = "Alternative destination",
                            sequence = 2,
                            estimatedArrivalAt = Now.AddMinutes(30),
                        },
                    },
                }));
            PendingActions.GetByIdForUpdateAsync(Action.Id, Arg.Any<CancellationToken>())
                .Returns(Action);
            Bookings.FindByIdForUpdateAsync(Booking.Id, Arg.Any<CancellationToken>())
                .Returns(Booking);
            UnitOfWork.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<ResolvePendingActionResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<ResolvePendingActionResult>>>()());
            Clock.UtcNow.Returns(Now);
            Handler = new ResolvePendingActionCommandHandler(
                PendingActions,
                Bookings,
                History,
                Outbox,
                UnitOfWork,
                Clock);
        }

        public IBookingPendingActionRepository PendingActions { get; } =
            Substitute.For<IBookingPendingActionRepository>();
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingStatusHistoryRepository History { get; } =
            Substitute.For<IBookingStatusHistoryRepository>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public BookingEntity Booking { get; }
        public BookingPendingAction Action { get; }
        public Guid CandidateStopId { get; }
        public ResolvePendingActionCommandHandler Handler { get; }

        public Task<ResolvePendingActionResult> HandleAsync(
            string action,
            Guid? selectedStopId = null,
            Guid? selectedStationId = null)
            => Handler.Handle(new ResolvePendingActionCommand(
                Booking.Id,
                Action.Id,
                Booking.PassengerUserId,
                Guid.NewGuid().ToString("D"),
                action,
                null,
                [],
                selectedStopId,
                selectedStationId), CancellationToken.None);

        private static BookingEntity CreateConfirmedBooking(long amount)
        {
            var booking = BookingEntity.CreatePendingPayment(
                BookingCode.Generate(Now),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Money.FromRaw(amount),
                Money.Zero,
                Money.FromRaw(amount),
                tripSnapshotDeparture: Now.AddHours(3));
            booking.Confirm(Now.AddHours(-1));
            return booking;
        }
    }
}
