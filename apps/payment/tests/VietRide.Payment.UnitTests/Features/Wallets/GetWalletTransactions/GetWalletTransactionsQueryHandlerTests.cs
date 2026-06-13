using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Wallets.GetWalletTransactions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.Wallets.GetWalletTransactions;

public sealed class GetWalletTransactionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenCreditTypeIsRequested_ReturnsPagedTransactionsAndPassesFiltersToRepository()
    {
        var userId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);
        var items = new[]
        {
            new GetWalletTransactionResult(
                Guid.NewGuid(),
                "CREDIT",
                100_000,
                50_000,
                150_000,
                "TOP_UP",
                Guid.NewGuid(),
                "Top-up succeeded",
                to.AddHours(-1)),
        };
        var expected = PagedResult<GetWalletTransactionResult>.Create(items, page: 2, pageSize: 10, totalItems: 12);
        var repository = new FakeWalletRepository(expected);
        var handler = new GetWalletTransactionsQueryHandler(repository);

        var result = await handler.Handle(
            new GetWalletTransactionsQuery(userId, from, to, "CREDIT", Page: 2, PageSize: 10),
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        result.Items.Should().BeEquivalentTo(items);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalItems.Should().Be(12);
        result.TotalPages.Should().Be(2);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
        repository.LastUserId.Should().Be(userId);
        repository.LastFrom.Should().Be(from);
        repository.LastTo.Should().Be(to);
        repository.LastType.Should().Be(WalletTransactionType.CREDIT);
        repository.LastPage.Should().Be(2);
        repository.LastPageSize.Should().Be(10);
        repository.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Handle_WhenTypeIsInvalid_ThrowsValidationException(string type)
    {
        var repository = new FakeWalletRepository(PagedResult<GetWalletTransactionResult>.Create([], 1, 20, 0));
        var handler = new GetWalletTransactionsQueryHandler(repository);

        var act = async () => await handler.Handle(
            new GetWalletTransactionsQuery(Guid.NewGuid(), null, null, type, Page: 1, PageSize: 20),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        repository.CallCount.Should().Be(0);
    }

    private sealed class FakeWalletRepository : IWalletRepository
    {
        private readonly PagedResult<GetWalletTransactionResult> _result;

        public FakeWalletRepository(PagedResult<GetWalletTransactionResult> result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }
        public Guid? LastUserId { get; private set; }
        public DateTimeOffset? LastFrom { get; private set; }
        public DateTimeOffset? LastTo { get; private set; }
        public WalletTransactionType? LastType { get; private set; }
        public int? LastPage { get; private set; }
        public int? LastPageSize { get; private set; }

        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Wallet?>(null);

        public Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(Wallet entity)
        {
        }

        public void Remove(Wallet entity)
        {
        }

        public IQueryable<Wallet> Query()
            => Enumerable.Empty<Wallet>().AsQueryable();

        public IQueryable<Wallet> QueryNoTracking()
            => Enumerable.Empty<Wallet>().AsQueryable();

        public Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<PagedResult<GetWalletTransactionResult>> GetUserWalletTransactionsAsync(
            Guid userId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            WalletTransactionType? type,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastUserId = userId;
            LastFrom = from;
            LastTo = to;
            LastType = type;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(_result);
        }

        public Task<WalletTransaction> CreditTopUpAsync(
            Guid userId,
            Money amount,
            Guid topUpRequestId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Get wallet transaction tests do not credit top-ups.");
    }
}
