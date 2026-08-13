using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;

namespace VietRide.Booking.UnitTests.Features.OperatorBookings;

public sealed class ListOperatorBookingsQueryHandlerTests
{
    private readonly IBookingRepository _repository = Substitute.For<IBookingRepository>();
    private readonly IIdentityUserServiceClient _identity = Substitute.For<IIdentityUserServiceClient>();

    [Fact]
    public async Task Handle_PassesTenantAllFiltersVietnamIntervalAndPagingToRepository()
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _identity.GetUserIdByPhoneAsync("+84901234567", Arg.Any<CancellationToken>())
            .Returns(userId);
        _repository.ListOperatorBookingsAsync(Arg.Any<OperatorBookingListCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage([], 0));
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        var result = await sut.Handle(new ListOperatorBookingsQuery(
            operatorId, "CONFIRMED,CANCELLED", tripId, new DateOnly(2026, 7, 11),
            " 0901234567 ", "  abc' OR 1=1 -- ", 3, 40, "totalAmount", "asc"), default);

        result.Should().BeEquivalentTo(new
        {
            Page = 3,
            PageSize = 40,
            TotalItems = 0L,
            TotalPages = 0,
            HasNextPage = false,
            HasPreviousPage = true,
        });
        await _repository.Received(1).ListOperatorBookingsAsync(
            Arg.Is<OperatorBookingListCriteria>(c =>
                c.OperatorId == operatorId
                && c.Statuses!.SequenceEqual(new[] { BookingStatus.CONFIRMED, BookingStatus.CANCELLED })
                && c.TripId == tripId
                && c.DepartureFrom == new DateTimeOffset(2026, 7, 10, 17, 0, 0, TimeSpan.Zero)
                && c.DepartureTo == new DateTimeOffset(2026, 7, 11, 17, 0, 0, TimeSpan.Zero)
                && c.PassengerUserId == userId
                && c.BookingCode == "abc' OR 1=1 --"
                && c.Page == 3 && c.PageSize == 40
                && c.SortBy == "totalAmount" && !c.SortDescending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DateFilter_UsesFixedVietnamMidnightIntervalRegardlessOfHostTimeZone()
    {
        _repository.ListOperatorBookingsAsync(Arg.Any<OperatorBookingListCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage([], 0));
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        await sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, new DateOnly(2026, 1, 15), null, null), default);

        await _repository.Received(1).ListOperatorBookingsAsync(
            Arg.Is<OperatorBookingListCriteria>(criteria =>
                criteria.DepartureFrom == new DateTimeOffset(2026, 1, 14, 17, 0, 0, TimeSpan.Zero)
                && criteria.DepartureTo == new DateTimeOffset(2026, 1, 15, 17, 0, 0, TimeSpan.Zero)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownPhone_ReturnsExactEmptySevenFieldPageWithoutRepositoryCall()
    {
        _identity.GetUserIdByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        var result = await sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, null, "+84901234567", null, 2, 20), default);

        result.Items.Should().BeEmpty();
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(20);
        result.TotalItems.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
        await _repository.DidNotReceive().ListOperatorBookingsAsync(
            Arg.Any<OperatorBookingListCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PropagatesIdentityFailureAndCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _identity.GetUserIdByPhoneAsync(Arg.Any<string>(), cts.Token)
            .Returns<Task<Guid?>>(_ => throw new OperationCanceledException(cts.Token));
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        var act = () => sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, null, "0901234567", null), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handle_PhoneInjectionPayloadIsRejectedBeforeCallingDependencies()
    {
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        var act = () => sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, null, "0901' OR 1=1 --", null), default);

        await act.Should().ThrowAsync<ArgumentException>();
        await _identity.DidNotReceive().GetUserIdByPhoneAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().ListOperatorBookingsAsync(
            Arg.Any<OperatorBookingListCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DefaultsToCreatedAtDescendingAndReturnsRepositoryPage()
    {
        var item = new OperatorBookingListItem(
            Guid.NewGuid(), "VR001", Guid.NewGuid(), "CONFIRMED",
            new OperatorBookingTripDto("R", "O", "D", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            2, 100_000, DateTimeOffset.UtcNow);
        _repository.ListOperatorBookingsAsync(Arg.Any<OperatorBookingListCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage([item], 21));
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        var result = await sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, null, null, null), default);

        result.Items.Should().ContainSingle().Which.Should().Be(item);
        result.TotalItems.Should().Be(21);
        result.TotalPages.Should().Be(2);
        result.HasNextPage.Should().BeTrue();
        await _repository.Received().ListOperatorBookingsAsync(
            Arg.Is<OperatorBookingListCriteria>(c => c.SortBy == "createdAt" && c.SortDescending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForwardsGeneralSearchAndNormalizedBuyerPhoneWithoutIdentityLookup()
    {
        _repository.ListOperatorBookingsAsync(
                Arg.Any<OperatorBookingListCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage([], 0));
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        await sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, null, null, null,
            Search: " 0901234567 "), default);

        await _identity.DidNotReceive().GetUserIdByPhoneAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).ListOperatorBookingsAsync(
            Arg.Is<OperatorBookingListCriteria>(criteria =>
                criteria.Search == "0901234567"
                && criteria.SearchPhone == "+84901234567"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidSortMapsThroughSharedFilterToHttp400InvalidSortField()
    {
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);
        var act = () => sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, null, null, null, SortBy: "passengerPhone"), default);
        var exception = (await act.Should().ThrowAsync<BadRequestException>()).Which;
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var exceptionContext = new ExceptionContext(actionContext, []) { Exception = exception };

        new ApiResponseExceptionFilter(NullLogger<ApiResponseExceptionFilter>.Instance)
            .OnException(exceptionContext);

        var objectResult = exceptionContext.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var envelope = objectResult.Value.Should().BeAssignableTo<ApiResponse>().Subject;
        envelope.Error!.Code.Should().Be("INVALID_SORT_FIELD");
    }

    [Fact]
    public async Task Handle_PageSize101ClampsRepositoryCriteriaAndPageResultTo100()
    {
        _repository.ListOperatorBookingsAsync(Arg.Any<OperatorBookingListCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage([], 101));
        var sut = new ListOperatorBookingsQueryHandler(_repository, _identity);

        var result = await sut.Handle(new ListOperatorBookingsQuery(
            Guid.NewGuid(), null, null, null, null, null, PageSize: 101), default);

        result.PageSize.Should().Be(100);
        result.TotalPages.Should().Be(2);
        await _repository.Received(1).ListOperatorBookingsAsync(
            Arg.Is<OperatorBookingListCriteria>(criteria => criteria.PageSize == 100),
            Arg.Any<CancellationToken>());
    }
}
