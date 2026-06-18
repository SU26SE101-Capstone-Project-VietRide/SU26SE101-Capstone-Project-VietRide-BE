using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Api.Controllers;
using VietRide.Payment.Api.Controllers.Requests;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.TopUps.CreateTopUp;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.TopUps.CreateTopUp;

public sealed class CreateTopUpCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenRequestIsValid_PersistsPendingTopUpAndReturnsRedirectUrl()
    {
        var repository = new FakeTopUpRequestRepository();
        var vnPayClient = new FakeVnPayClient("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=fake&vnp_SecureHash=abc");
        var clock = new FrozenClock(new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero));
        var handler = new CreateTopUpCommandHandler(
            repository,
            vnPayClient,
            clock,
            NullLogger<CreateTopUpCommandHandler>.Instance);
        var userId = Guid.NewGuid();

        var result = await handler.Handle(
            new CreateTopUpCommand(userId, 100_000, "VNPAY", "203.0.113.10"),
            CancellationToken.None);

        repository.TopUpRequests.Should().ContainSingle();
        var topUpRequest = repository.TopUpRequests.Single();
        topUpRequest.Id.Should().Be(result.TopUpRequestId);
        topUpRequest.UserId.Should().Be(userId);
        topUpRequest.Amount.Should().Be(Money.FromRaw(100_000));
        topUpRequest.Status.ToString().Should().Be("PENDING");
        topUpRequest.VnPayTxnRef.Should().NotBeEmpty();
        Guid.TryParse(topUpRequest.VnPayTxnRef, out _).Should().BeTrue();
        topUpRequest.PaymentRedirectUrl.Should().Be(vnPayClient.RedirectUrl);
        result.Status.Should().Be("PENDING");
        result.PaymentRedirectUrl.Should().Be(vnPayClient.RedirectUrl);
        vnPayClient.LastAmount.Should().Be(Money.FromRaw(100_000));
        vnPayClient.LastUserId.Should().Be(userId);
        vnPayClient.LastClientIpAddress.Should().Be("203.0.113.10");
        vnPayClient.LastCreatedAt.Should().Be(clock.UtcNow);
    }

    [Fact]
    public async Task CreateTopUp_WhenIdempotencyKeyIsMissing_ThrowsValidationBeforeSendingCommand()
    {
        var sender = new RecordingSender();
        var controller = new WalletController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var act = async () => await controller.CreateTopUp(
            new CreateTopUpRequest(100_000, "VNPAY"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "Idempotency-Key");
        sender.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenAmountIsBelowMinimum_ThrowsRegisteredWalletTopUpError()
    {
        var repository = new FakeTopUpRequestRepository();
        var handler = new CreateTopUpCommandHandler(
            repository,
            new FakeVnPayClient("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"),
            new FrozenClock(DateTimeOffset.UtcNow),
            NullLogger<CreateTopUpCommandHandler>.Instance);

        var act = async () => await handler.Handle(
            new CreateTopUpCommand(Guid.NewGuid(), 9_999, "VNPAY", "203.0.113.10"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("WALLET_TOP_UP_AMOUNT_TOO_LOW");
        repository.TopUpRequests.Should().BeEmpty();
    }

    private sealed class RecordingSender : ISender
    {
        public bool WasCalled { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult((TResponse)(object)new CreateTopUpResult(Guid.NewGuid(), "PENDING", "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"));
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<object?>(new CreateTopUpResult(Guid.NewGuid(), "PENDING", "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"));
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Create top-up tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Create top-up tests do not use streaming MediatR requests.");
    }

    private sealed class FakeVnPayClient : IVnPayClient
    {
        public FakeVnPayClient(string redirectUrl)
        {
            RedirectUrl = redirectUrl;
        }

        public string RedirectUrl { get; }
        public Guid LastUserId { get; private set; }
        public Money LastAmount { get; private set; }
        public string? LastClientIpAddress { get; private set; }
        public DateTimeOffset LastCreatedAt { get; private set; }

        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt)
        {
            vnPayTxnRef.Should().NotBeEmpty();
            LastUserId = userId;
            LastAmount = amount;
            LastClientIpAddress = clientIpAddress;
            LastCreatedAt = createdAt;
            return RedirectUrl;
        }
    }

    private sealed class FakeTopUpRequestRepository : ITopUpRequestRepository
    {
        private readonly List<TopUpRequest> _topUpRequests = [];

        public IReadOnlyList<TopUpRequest> TopUpRequests => _topUpRequests;

        public Task<TopUpRequest?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_topUpRequests.FirstOrDefault(topUp => topUp.Id == id));

        public Task<TopUpRequest> AddAsync(TopUpRequest entity, CancellationToken ct)
        {
            _topUpRequests.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TopUpRequest entity)
        {
        }

        public void Remove(TopUpRequest entity)
            => _topUpRequests.Remove(entity);

        public IQueryable<TopUpRequest> Query()
            => _topUpRequests.AsQueryable();

        public IQueryable<TopUpRequest> QueryNoTracking()
            => _topUpRequests.AsQueryable();

        public Task<TopUpRequest?> FindByVnPayTxnRefAsync(string vnPayTxnRef, CancellationToken cancellationToken)
            => Task.FromResult(_topUpRequests.FirstOrDefault(topUp => topUp.VnPayTxnRef == vnPayTxnRef));
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
