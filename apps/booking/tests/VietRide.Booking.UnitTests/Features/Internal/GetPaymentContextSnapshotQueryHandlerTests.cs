using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Internal;

public sealed class GetPaymentContextSnapshotQueryHandlerTests
{
    [Fact]
    public async Task Handle_Booking_ReturnsReferenceCodeInAllocationAndWireShape()
    {
        const string bookingCode = "VR-20260810-ABCD2345";
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Restore(bookingCode),
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            null,
            null,
            null,
            Money.FromRaw(200_000),
            Money.Zero,
            Money.FromRaw(200_000));
        var bookings = Substitute.For<IBookingRepository>();
        bookings.QueryNoTracking().Returns(new TestAsyncEnumerable<BookingEntity>([booking]));
        var handler = new GetPaymentContextSnapshotQueryHandler(bookings);

        var result = await handler.Handle(
            new GetPaymentContextSnapshotQuery("BOOKING", booking.Id),
            CancellationToken.None);

        result.CanBackfill.Should().BeTrue();
        result.Allocations.Should().ContainSingle()
            .Which.ReferenceCode.Should().Be(bookingCode);

        var json = JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("allocations")[0]
            .GetProperty("referenceCode")
            .GetString()
            .Should().Be(bookingCode);
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        public TestAsyncQueryProvider(IQueryProvider inner)
        {
            _inner = inner;
        }

        public IQueryable CreateQuery(Expression expression)
            => new TestAsyncEnumerable<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            => new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression)
            => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression)
            => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(
            Expression expression,
            CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments().Single();
            var result = Execute(expression);

            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result])!;
        }
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable)
            : base(enumerable)
        {
        }

        public TestAsyncEnumerable(Expression expression)
            : base(expression)
        {
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner)
        {
            _inner = inner;
        }

        public T Current => _inner.Current;

        public ValueTask<bool> MoveNextAsync()
            => ValueTask.FromResult(_inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
