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

        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

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
    }

    [Fact]
    public async Task Create_Returns_RouteOwnershipUnverifiable_WhenTripClientFails()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
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
    public async Task Create_FloorsPriceToNearest1000()
    {
        var repo = Substitute.For<IParcelRouteFareRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var uow = Substitute.For<IUnitOfWork>();
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

        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var handler = new CreateParcelRouteFareCommandHandler(repo, tripClient, uow, clock);
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "LARGE", 123_500, Now, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.PriceVnd.Should().Be(123_000);
        captured!.PriceVnd.Amount.Should().Be(123_000);
    }

    [Fact]
    public async Task Create_FlooredPrice999_ThrowsValidationError()
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
        var command = new CreateParcelRouteFareCommand(OperatorId, RouteId, "SMALL", 999, Now, null);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    #endregion

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
            CreateFare(Guid.NewGuid(), ParcelSizeCategory.MEDIUM, operatorA, 80_000, Now),
            CreateFare(Guid.NewGuid(), ParcelSizeCategory.LARGE, operatorB, 100_000, Now),
        };

        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(operatorA, null, null, 1, 20);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalItems.Should().Be(2);
        result.Items.Should().AllSatisfy(r => r.OperatorId.Should().Be(operatorA));
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
        result.Items.Single().SizeCategory.Should().Be("SMALL");
    }

    [Fact]
    public async Task List_SupportsPagination()
    {
        var fares = Enumerable.Range(0, 25)
            .Select(i => CreateFare(
                Guid.NewGuid(),
                (ParcelSizeCategory)(i % 4),
                OperatorId,
                50_000 + i * 1000,
                Now.AddDays(-i)))
            .ToList();

        var repo = Substitute.For<IParcelRouteFareRepository>();
        repo.QueryNoTracking().Returns(fares.AsAsyncQueryable());

        var handler = new ListParcelRouteFaresQueryHandler(repo);
        var query = new ListParcelRouteFaresQuery(OperatorId, null, null, 2, 10);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(10);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalItems.Should().Be(25);
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
    public async Task Update_FlooredPrice999_ThrowsValidationError()
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
        var command = new UpdateParcelRouteFareCommand(OperatorId, RouteId, "MEDIUM", 999, null, null);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(command, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    #endregion

    private static ParcelRouteFare CreateFare(Guid routeId, ParcelSizeCategory size, Guid operatorId,
        long price, DateTimeOffset effectiveFrom)
    {
        var fare = ParcelRouteFare.Create(routeId, size, operatorId, Money.FromRaw(price), effectiveFrom);
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
