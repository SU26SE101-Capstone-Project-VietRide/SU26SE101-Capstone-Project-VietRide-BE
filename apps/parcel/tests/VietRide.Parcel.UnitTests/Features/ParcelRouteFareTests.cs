using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Create;
using VietRide.Parcel.Application.Features.ParcelRouteFares.List;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Update;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelRouteFareTests
{
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 29, 10, 0, 0, TimeSpan.FromHours(7));

    #region Create

    [Fact]
    public async Task Create_Success_WithValidData_ReturnsResponse()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
        ConfigureTransactions(uow);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        repo.FindByCompositeAsync(Arg.Any<Guid>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<CancellationToken>())
            .Returns((ParcelRouteFare?)null);

        ParcelRouteFare? captured = null;
        repo.AddAsync(Arg.Any<ParcelRouteFare>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<ParcelRouteFare>(0);
                return Task.FromResult(captured);
            });

        var handler = new CreateParcelRouteFareCommandHandler(repo, tripClient, uow, clock);
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 100_000, Now, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.RouteId.Should().Be(RouteId);
        result.SizeCategory.Should().Be("MEDIUM");
        result.OperatorId.Should().Be(OperatorId);
        result.PriceVnd.Should().Be(100_000);
        result.EffectiveFrom.Should().Be(Now);

        captured.Should().NotBeNull();
        captured!.PriceVnd.Amount.Should().Be(100_000);
        await repo.Received(1).AcquireRouteBatchLockAsync(RouteId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Returns_RouteOwnershipUnverifiable_WhenTripClientFails()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
        ConfigureTransactions(uow);
        var clock = Substitute.For<IClock>();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.TransportError, "upstream down"));

        var handler = new CreateParcelRouteFareCommandHandler(repo, tripClient, uow, clock);
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 100_000, Now, null);

        var ex = await Assert.ThrowsAsync<ParcelDependencyUnavailableException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("ROUTE_OWNERSHIP_UNVERIFIABLE");
    }

    [Fact]
    public async Task Create_Returns_RouteNotFound_WhenRouteNotOwned()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
        ConfigureTransactions(uow);
        var clock = Substitute.For<IClock>();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.RouteNotFound, null));

        var handler = new CreateParcelRouteFareCommandHandler(repo, tripClient, uow, clock);
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 100_000, Now, null);

        var ex = await Assert.ThrowsAsync<CodedNotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task Create_Returns_FareAlreadyExists_WhenDuplicate()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
        ConfigureTransactions(uow);
        var clock = Substitute.For<IClock>();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        var existing = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.MEDIUM, OperatorId,
            Money.FromRaw(80_000), Now);
        repo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(existing);

        var handler = new CreateParcelRouteFareCommandHandler(repo, tripClient, uow, clock);
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 100_000, Now, null);

        var ex = await Assert.ThrowsAsync<CodedConflictException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("FARE_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Create_StoresExactPriceVnd()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
        ConfigureTransactions(uow);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        repo.FindByCompositeAsync(Arg.Any<Guid>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<CancellationToken>())
            .Returns((ParcelRouteFare?)null);

        ParcelRouteFare? captured = null;
        repo.AddAsync(Arg.Any<ParcelRouteFare>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<ParcelRouteFare>(0);
                return Task.FromResult(captured);
            });

        var handler = new CreateParcelRouteFareCommandHandler(repo, tripClient, uow, clock);
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "LARGE", 123_500, Now, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.PriceVnd.Should().Be(123_500);
        captured!.PriceVnd.Amount.Should().Be(123_500);
    }

    [Fact]
    public async Task Create_NonPositivePrice_ThrowsValidationError()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        repo.FindByCompositeAsync(Arg.Any<Guid>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<CancellationToken>())
            .Returns((ParcelRouteFare?)null);

        var handler = new CreateParcelRouteFareCommandHandler(repo, tripClient, uow, clock);
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "SMALL", 0, Now, null);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    #endregion

    private static void ConfigureTransactions(IUnitOfWork unitOfWork)
        => unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<Task<ParcelRouteFareResponse>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task<ParcelRouteFareResponse>>>()());

    #region List

    [Fact]
    public async Task List_FiltersByOperatorId()
    {
        var operatorA = Guid.NewGuid();
        var operatorB = Guid.NewGuid();
        var routeId = Guid.NewGuid();

        var fares = new List<ParcelRouteFare>
        {
            CreateFare(routeId, ParcelSizeCategory.SMALL, operatorA, 50_000, Now),
            CreateFare(routeId, ParcelSizeCategory.MEDIUM, operatorA, 80_000, Now),
            CreateFare(Guid.NewGuid(), ParcelSizeCategory.MEDIUM, operatorA, 80_000, Now),
            CreateFare(routeId, ParcelSizeCategory.LARGE, operatorB, 100_000, Now),
        };

        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(operatorA, null, null, 1, 20);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalItems.Should().Be(2);
        result.Items.Single(item => item.RouteId == routeId).Fares
            .Select(fare => fare.SizeCategory)
            .Should().Equal("SMALL", "MEDIUM");
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task List_FiltersByRouteIdAndSizeCategory()
    {
        var routeId = Guid.NewGuid();

        var fares = new List<ParcelRouteFare>
        {
            CreateFare(routeId, ParcelSizeCategory.SMALL, OperatorId, 50_000, Now),
            CreateFare(routeId, ParcelSizeCategory.MEDIUM, OperatorId, 80_000, Now),
            CreateFare(routeId, ParcelSizeCategory.LARGE, OperatorId, 100_000, Now),
            CreateFare(Guid.NewGuid(), ParcelSizeCategory.SMALL, OperatorId, 60_000, Now),
        };

        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(OperatorId, routeId, "SMALL", 1, 20);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.TotalItems.Should().Be(1);
        result.Items.Single().RouteId.Should().Be(routeId);
        result.Items.Single().Fares.Select(fare => fare.SizeCategory)
            .Should().Equal("SMALL", "MEDIUM", "LARGE");
    }

    [Fact]
    public async Task List_SupportsPagination()
    {
        var routeIds = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        var fares = routeIds
            .SelectMany((routeId, routeIndex) => Enum.GetValues<ParcelSizeCategory>()
                .Select(sizeCategory => CreateFare(
                    routeId,
                    sizeCategory,
                    OperatorId,
                    50_000 + routeIndex * 1000 + (int)sizeCategory,
                    Now.AddDays(-routeIndex))))
            .ToList();

        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(OperatorId, null, null, 2, 5);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(5);
        result.Items.Should().OnlyHaveUniqueItems(item => item.RouteId);
        result.Items.Should().AllSatisfy(item => item.Fares.Should().HaveCount(4));
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(5);
        result.TotalItems.Should().Be(12);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task List_PageZero_ThrowsValidationError()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(OperatorId, null, null, 0, 20);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task List_PageSizeZero_ThrowsValidationError()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(OperatorId, null, null, 1, 0);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task List_PageSizeExceedsMax_ThrowsValidationError()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(OperatorId, null, null, 1, 101);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task List_SearchFiltersByRouteIdsBeforeCountAndPaging()
    {
        var matchingRouteId = Guid.NewGuid();
        var otherRouteId = Guid.NewGuid();
        var fares = new[]
        {
            ParcelRouteFare.Create(matchingRouteId, ParcelSizeCategory.SMALL, OperatorId, Money.FromRaw(10000), Now, null),
            ParcelRouteFare.Create(matchingRouteId, ParcelSizeCategory.MEDIUM, OperatorId, Money.FromRaw(15000), Now, null),
            ParcelRouteFare.Create(otherRouteId, ParcelSizeCategory.SMALL, OperatorId, Money.FromRaw(20000), Now, null),
        };
        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.SearchRoutesAsync(OperatorId, "Da Lat", Arg.Any<CancellationToken>())
            .Returns(RouteSearchOutcome.Success([matchingRouteId]));

        var result = await new ListParcelRouteFaresQueryHandler(repo, tripClient).Handle(
            new ListParcelRouteFaresQuery(OperatorId, null, null, 1, 20, "Da Lat"),
            CancellationToken.None);

        result.TotalItems.Should().Be(1);
        result.Items.Should().ContainSingle(item => item.RouteId == matchingRouteId);
        result.Items.Single().Fares.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_ReturnsCanonicalFareOrderAndPreservesMixedEffectiveWindows()
    {
        var routeId = Guid.NewGuid();
        var smallUntil = Now.AddMonths(1);
        var fares = new[]
        {
            CreateFare(routeId, ParcelSizeCategory.EXTRA_LARGE, OperatorId, 40_000, Now.AddDays(3)),
            CreateFare(routeId, ParcelSizeCategory.SMALL, OperatorId, 10_000, Now, smallUntil),
            CreateFare(routeId, ParcelSizeCategory.LARGE, OperatorId, 30_000, Now.AddDays(2)),
            CreateFare(routeId, ParcelSizeCategory.MEDIUM, OperatorId, 20_000, Now.AddDays(1)),
        };
        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());

        var result = await new ListParcelRouteFaresQueryHandler(repo).Handle(
            new ListParcelRouteFaresQuery(OperatorId, routeId, null, 1, 20),
            CancellationToken.None);

        var groupedFares = result.Items.Single().Fares;
        groupedFares.Select(fare => fare.SizeCategory)
            .Should().Equal("SMALL", "MEDIUM", "LARGE", "EXTRA_LARGE");
        groupedFares.Single(fare => fare.SizeCategory == "SMALL").EffectiveUntil
            .Should().Be(smallUntil);
        groupedFares.Single(fare => fare.SizeCategory == "EXTRA_LARGE").EffectiveFrom
            .Should().Be(Now.AddDays(3));
    }

    [Fact]
    public async Task List_EffectiveFromSortAggregatesQualifyingFaresAndUsesRouteIdTieBreak()
    {
        var lowerRouteId = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var higherRouteId = Guid.Parse("20000000-0000-0000-0000-000000000000");
        var newestRouteId = Guid.Parse("30000000-0000-0000-0000-000000000000");
        var fares = new[]
        {
            CreateFare(lowerRouteId, ParcelSizeCategory.SMALL, OperatorId, 10_000, Now),
            CreateFare(lowerRouteId, ParcelSizeCategory.MEDIUM, OperatorId, 20_000, Now.AddDays(1)),
            CreateFare(higherRouteId, ParcelSizeCategory.SMALL, OperatorId, 11_000, Now),
            CreateFare(higherRouteId, ParcelSizeCategory.MEDIUM, OperatorId, 21_000, Now.AddDays(1)),
            CreateFare(newestRouteId, ParcelSizeCategory.SMALL, OperatorId, 12_000, Now.AddDays(2)),
        };
        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());
        var handler = new ListParcelRouteFaresQueryHandler(repo);

        var descending = await handler.Handle(
            new ListParcelRouteFaresQuery(
                OperatorId, null, null, 1, 20, SortBy: "effectiveFrom", SortDir: "desc"),
            CancellationToken.None);
        var ascending = await handler.Handle(
            new ListParcelRouteFaresQuery(
                OperatorId, null, null, 1, 20, SortBy: "effectiveFrom", SortDir: "asc"),
            CancellationToken.None);

        descending.Items.Select(item => item.RouteId)
            .Should().Equal(newestRouteId, higherRouteId, lowerRouteId);
        ascending.Items.Select(item => item.RouteId)
            .Should().Equal(lowerRouteId, higherRouteId, newestRouteId);
    }

    [Fact]
    public async Task List_EffectiveFilterSelectsRoutesButReturnsEveryPersistedFareForTheRoute()
    {
        var matchingRouteId = Guid.NewGuid();
        var scheduledOnlyRouteId = Guid.NewGuid();
        var fares = new[]
        {
            CreateFare(
                matchingRouteId,
                ParcelSizeCategory.SMALL,
                OperatorId,
                10_000,
                Now.AddDays(-1)),
            CreateFare(
                matchingRouteId,
                ParcelSizeCategory.MEDIUM,
                OperatorId,
                20_000,
                Now.AddDays(2)),
            CreateFare(
                scheduledOnlyRouteId,
                ParcelSizeCategory.SMALL,
                OperatorId,
                30_000,
                Now.AddDays(3)),
        };
        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());

        var result = await new ListParcelRouteFaresQueryHandler(repo).Handle(
            new ListParcelRouteFaresQuery(
                OperatorId,
                null,
                null,
                1,
                20,
                EffectiveAt: new DateOnly(2026, 6, 29),
                Status: "ACTIVE"),
            CancellationToken.None);

        result.TotalItems.Should().Be(1);
        result.Items.Should().ContainSingle(item => item.RouteId == matchingRouteId);
        result.Items.Single().Fares.Select(fare => fare.SizeCategory)
            .Should().Equal("SMALL", "MEDIUM");
    }

    [Fact]
    public async Task List_SearchWithoutMatchingRoutesReturnsEmptyPage()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(Array.Empty<ParcelRouteFare>().AsAsyncQueryable());
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.SearchRoutesAsync(OperatorId, "No Match", Arg.Any<CancellationToken>())
            .Returns(RouteSearchOutcome.Success([]));

        var result = await new ListParcelRouteFaresQueryHandler(repo, tripClient).Handle(
            new ListParcelRouteFaresQuery(OperatorId, null, null, 1, 20, "No Match"),
            CancellationToken.None);

        result.TotalItems.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_SearchWhenTripIsUnavailableReturnsUpstreamUnavailable()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(Array.Empty<ParcelRouteFare>().AsAsyncQueryable());
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.SearchRoutesAsync(OperatorId, "Da Lat", Arg.Any<CancellationToken>())
            .Returns(RouteSearchOutcome.Failure("Trip unavailable"));

        var action = () => new ListParcelRouteFaresQueryHandler(repo, tripClient).Handle(
            new ListParcelRouteFaresQuery(OperatorId, null, null, 1, 20, "Da Lat"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    #endregion

    #region Update

    [Fact]
    public async Task Update_Success_UpdatesPrice()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        var existing = CreateFare(RouteId, ParcelSizeCategory.MEDIUM, OperatorId, 80_000, Now);
        repo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(existing);

        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var handler = new UpdateParcelRouteFareCommandHandler(repo, tripClient, uow);
        var command = new UpdateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 200_000, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.PriceVnd.Should().Be(200_000);
        existing.PriceVnd.Amount.Should().Be(200_000);
    }

    [Fact]
    public async Task Update_Returns_FareNotFound_WhenCompositeNotFound()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        repo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns((ParcelRouteFare?)null);

        var handler = new UpdateParcelRouteFareCommandHandler(repo, tripClient, uow);
        var command = new UpdateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 200_000, null, null);

        var ex = await Assert.ThrowsAsync<CodedNotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("FARE_NOT_FOUND");
    }

    [Fact]
    public async Task Update_Returns_FareNotFound_WhenCrossTenant()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();

        var otherOperatorId = Guid.NewGuid();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        var existing = CreateFare(RouteId, ParcelSizeCategory.MEDIUM, otherOperatorId, 80_000, Now);
        repo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(existing);

        var handler = new UpdateParcelRouteFareCommandHandler(repo, tripClient, uow);
        var command = new UpdateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 200_000, null, null);

        var ex = await Assert.ThrowsAsync<CodedNotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("FARE_NOT_FOUND");
    }

    [Fact]
    public async Task Update_NonPositivePrice_ThrowsValidationError()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();

        tripClient.ValidateRouteOwnershipAsync(RouteId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));

        var existing = CreateFare(RouteId, ParcelSizeCategory.MEDIUM, OperatorId, 80_000, Now);
        repo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(existing);

        var handler = new UpdateParcelRouteFareCommandHandler(repo, tripClient, uow);
        var command = new UpdateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 0, null, null);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    #endregion

    private static ParcelRouteFare CreateFare(
        Guid routeId,
        ParcelSizeCategory size,
        Guid operatorId,
        long price,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil = null)
    {
        var fare = ParcelRouteFare.Create(
            routeId,
            size,
            operatorId,
            Money.FromRaw(price),
            effectiveFrom,
            effectiveUntil);
        fare.CreatedAt = Now;
        fare.UpdatedAt = Now;
        return fare;
    }
}

internal static class AsyncQueryableExtensions
{
    public static IQueryable<T> AsAsyncQueryable<T>(this IEnumerable<T> source) where T : class
    {
        var queryable = source.AsQueryable();
        return new TestAsyncEnumerable<T>(queryable);
    }
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
    public TestAsyncEnumerable(Expression expression) : base(expression) { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

internal class TestAsyncQueryProvider<T> : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;

    public TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

    public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<T>(expression);
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new TestAsyncEnumerable<TElement>(expression);

    public object? Execute(Expression expression) => _inner.Execute(expression);
    public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
    {
        var syncResult = _inner.Execute(expression);
        var resultType = typeof(TResult).GetGenericArguments()[0];
        var fromResult = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Task.FromResult) && m.IsGenericMethodDefinition);
        var genericFromResult = fromResult.MakeGenericMethod(resultType);
        return (TResult)genericFromResult.Invoke(null, [syncResult])!;
    }
}

internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
    public T Current => _inner.Current;

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
}
