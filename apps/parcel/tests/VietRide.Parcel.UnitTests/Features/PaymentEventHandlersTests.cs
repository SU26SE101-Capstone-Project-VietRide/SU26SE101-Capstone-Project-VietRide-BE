using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;
using VietRide.Parcel.Application.Features.Parcels.ExpirePaymentForParcel;
using VietRide.Parcel.Application.Features.Parcels.FailPaymentForParcel;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class PaymentEventHandlersTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid PaymentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 29, 10, 0, 0, TimeSpan.FromHours(7));

    private static ParcelPaymentTransitionSnapshot MakeSnapshot(ParcelStatus status, long deposit = 100_000, long additional = 0)
        => new(ParcelId, "VRP-001", status, deposit, additional, Guid.NewGuid(),
            Guid.NewGuid(), null, Guid.NewGuid(), ParcelSizeCategory.MEDIUM, null);

    [Fact]
    public async Task ConfirmPaymentForParcel_PARCEL_DepositSucceeded()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositSucceededAsync(ParcelId, 100_000, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.PENDING));

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId, 100_000), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_PARCEL_ADDITIONAL_Succeeded()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkAdditionalSucceededAsync(ParcelId, 50_000, Arg.Any<Guid>(), Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.PENDING));

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL_ADDITIONAL", ParcelId, 50_000), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_UnrelatedReferenceType_ReturnsFalse()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var handler = new ConfirmPaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "BOOKING", ParcelId, 100_000), default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_DepositAlreadySucceeded_ReturnsFalse()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositSucceededAsync(ParcelId, 100_000, Now, Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId, 100_000), default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task FailPaymentForParcel_PARCEL_DepositExpired()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositFailedAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.EXPIRED));

        var handler = new FailPaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<FailPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new FailPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task FailPaymentForParcel_PARCEL_ADDITIONAL_Rejected()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkAdditionalFailedAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.REJECTED));

        var handler = new FailPaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<FailPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new FailPaymentForParcelCommand(PaymentId, "PARCEL_ADDITIONAL", ParcelId), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task FailPaymentForParcel_UnrelatedReferenceType_ReturnsFalse()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var handler = new FailPaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<FailPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new FailPaymentForParcelCommand(PaymentId, "BOOKING", ParcelId), default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExpirePaymentForParcel_PARCEL_DepositExpired()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositExpiredAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.EXPIRED));

        var handler = new ExpirePaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<ExpirePaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ExpirePaymentForParcelCommand(PaymentId, "PARCEL", ParcelId), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExpirePaymentForParcel_PARCEL_ADDITIONAL_Rejected()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkAdditionalExpiredAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.REJECTED));

        var handler = new ExpirePaymentForParcelCommandHandler(repo, clock,
            Substitute.For<ILogger<ExpirePaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ExpirePaymentForParcelCommand(PaymentId, "PARCEL_ADDITIONAL", ParcelId), default);

        result.Should().BeTrue();
    }
}
