using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Batch;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Behaviors;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class BatchParcelRouteFareTests
{
    private static readonly Guid OperatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RouteId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset EffectiveFrom =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void BatchParcelRouteFare_Validator_AcceptsBoundaryItemCounts(int count)
    {
        var command = ValidCommand(Enumerable.Range(0, count)
            .Select(index => new BatchParcelRouteFareItem(
                ((ParcelSizeCategory)index).ToString(),
                50_000L + index))
            .ToArray());

        var result = new BatchParcelRouteFareCommandValidator().Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void BatchParcelRouteFare_Validator_RejectsOutOfRangeItemCounts(int count)
    {
        var command = ValidCommand(Enumerable.Range(0, count)
            .Select(index => new BatchParcelRouteFareItem(
                ((ParcelSizeCategory)(index % 4)).ToString(),
                50_000L + index))
            .ToArray());

        var result = new BatchParcelRouteFareCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Items");
    }

    [Fact]
    public void BatchParcelRouteFare_Validator_RejectsDuplicateCategoriesCaseInsensitively()
    {
        var command = ValidCommand(
        [
            new BatchParcelRouteFareItem("SMALL", 50_000),
            new BatchParcelRouteFareItem("small", 60_000),
        ]);

        var result = new BatchParcelRouteFareCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Items");
    }

    [Theory]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    [InlineData("0")]
    public void BatchParcelRouteFare_Validator_RejectsNonCurrentEnumValues(string sizeCategory)
    {
        var command = ValidCommand([new BatchParcelRouteFareItem(sizeCategory, 50_000)]);

        var result = new BatchParcelRouteFareCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName.Contains("SizeCategory"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BatchParcelRouteFare_Validator_RejectsNonPositiveWholeVnd(long priceVnd)
    {
        var command = ValidCommand([new BatchParcelRouteFareItem("SMALL", priceVnd)]);

        var result = new BatchParcelRouteFareCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName.Contains("PriceVnd"));
    }

    [Fact]
    public void BatchParcelRouteFare_Validator_AcceptsOneVndAndMinimumIncreasingWindow()
    {
        var command = ValidCommand(
            [new BatchParcelRouteFareItem("EXTRA_LARGE", 1)],
            EffectiveFrom.AddTicks(1));

        var result = new BatchParcelRouteFareCommandValidator().Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BatchParcelRouteFare_Validator_RejectsNonIncreasingEffectiveWindow(int minuteOffset)
    {
        var command = ValidCommand(
            [new BatchParcelRouteFareItem("SMALL", 50_000)],
            EffectiveFrom.AddMinutes(minuteOffset));

        var result = new BatchParcelRouteFareCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "EffectiveUntil");
    }

    [Fact]
    public async Task BatchParcelRouteFare_Handler_UpdatesPhysicalRowAndCreatesInRequestOrder()
    {
        var repository = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var previousOperatorId = Guid.NewGuid();
        var existing = ParcelRouteFare.Create(
            RouteId,
            ParcelSizeCategory.MEDIUM,
            previousOperatorId,
            Money.FromRaw(70_000),
            EffectiveFrom.AddDays(-1));
        var requestedCategories = new[] { ParcelSizeCategory.MEDIUM, ParcelSizeCategory.SMALL };

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));
        repository.FindByRouteAndSizesAsync(
                RouteId,
                Arg.Is<IReadOnlyCollection<ParcelSizeCategory>>(values =>
                    values.SequenceEqual(requestedCategories)),
                Arg.Any<CancellationToken>())
            .Returns([existing]);
        repository.AddRangeAsync(
                Arg.Any<IReadOnlyCollection<ParcelRouteFare>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<Task<BatchParcelRouteFareResponse>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<Task<BatchParcelRouteFareResponse>>>(0)());
        var command = ValidCommand(
        [
            new BatchParcelRouteFareItem("MEDIUM", 80_000),
            new BatchParcelRouteFareItem("SMALL", 50_000),
        ]);
        var handler = new BatchParcelRouteFareCommandHandler(repository, tripClient, unitOfWork);

        var response = await handler.Handle(command, CancellationToken.None);

        response.RouteId.Should().Be(RouteId);
        response.Items.Select(item => item.SizeCategory).Should().Equal("MEDIUM", "SMALL");
        response.Items.Select(item => item.Created).Should().Equal(false, true);
        response.Items.Select(item => item.PriceVnd).Should().Equal(80_000, 50_000);
        response.Items.Should().OnlyContain(item => item.EffectiveFrom == EffectiveFrom.ToUniversalTime());
        existing.PriceVnd.Amount.Should().Be(80_000);
        existing.OperatorId.Should().Be(OperatorId);
        existing.EffectiveFrom.Should().Be(EffectiveFrom.ToUniversalTime());
        await repository.Received(1).AcquireRouteBatchLockAsync(
            RouteId,
            Arg.Any<CancellationToken>());
        await repository.Received(1).FindByRouteAndSizesAsync(
            RouteId,
            Arg.Any<IReadOnlyCollection<ParcelSizeCategory>>(),
            Arg.Any<CancellationToken>());
        await repository.Received(1).AddRangeAsync(
            Arg.Is<IReadOnlyCollection<ParcelRouteFare>>(rows =>
                rows.Count == 1
                && rows.Single().OperatorId == OperatorId
                && rows.Single().RouteId == RouteId
                && rows.Single().SizeCategory == ParcelSizeCategory.SMALL),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<Task<BatchParcelRouteFareResponse>>>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task BatchParcelRouteFare_Handler_RouteNotOwned_DoesNotReadOrMutateFares()
    {
        var repository = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.RouteNotFound, null));
        var handler = new BatchParcelRouteFareCommandHandler(repository, tripClient, unitOfWork);

        var action = () => handler.Handle(ValidCommand(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
        await repository.DidNotReceiveWithAnyArgs().FindByRouteAndSizesAsync(
            default,
            default!,
            default);
        await repository.DidNotReceiveWithAnyArgs().AddRangeAsync(default!, default);
        await unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<BatchParcelRouteFareResponse>(
            default!,
            default);
    }

    [Fact]
    public async Task BatchParcelRouteFare_Handler_OwnershipTransportFailure_DoesNotReadOrMutateFares()
    {
        var repository = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.TransportError, "trip unavailable"));
        var handler = new BatchParcelRouteFareCommandHandler(repository, tripClient, unitOfWork);

        var action = () => handler.Handle(ValidCommand(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_OWNERSHIP_UNVERIFIABLE");
        await repository.DidNotReceiveWithAnyArgs().FindByRouteAndSizesAsync(
            default,
            default!,
            default);
        await unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<BatchParcelRouteFareResponse>(
            default!,
            default);
    }

    [Fact]
    public void BatchParcelRouteFare_CommandSkipsAmbientTransactionDuringOwnershipPreflight()
    {
        typeof(BatchParcelRouteFareCommand)
            .GetCustomAttributes(typeof(SkipTransactionAttribute), inherit: false)
            .Should().ContainSingle();
    }

    private static BatchParcelRouteFareCommand ValidCommand(
        IReadOnlyList<BatchParcelRouteFareItem>? items = null,
        DateTimeOffset? effectiveUntil = null)
        => new(
            OperatorId,
            RouteId,
            EffectiveFrom,
            effectiveUntil,
            items ?? [new BatchParcelRouteFareItem("SMALL", 50_000)]);
}
