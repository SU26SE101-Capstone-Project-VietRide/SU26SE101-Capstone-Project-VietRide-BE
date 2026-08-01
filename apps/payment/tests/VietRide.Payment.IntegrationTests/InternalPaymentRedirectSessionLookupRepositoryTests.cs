using System.Data.Common;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Payment.Application;
using VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Shared.Application.DependencyInjection;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.DependencyInjection;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.IntegrationTests;

public sealed class InternalPaymentRedirectSessionLookupRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-01T08:00:00Z");

    [Fact]
    public async Task InternalPaymentRedirectSessionLookup_SelectsLatestBeforeEligibilityInOneNoTrackingQuery()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_PERSISTENCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var interceptor = new CountingCommandInterceptor();
        await using var provider = CreateProvider(connectionString, interceptor);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var userId = Guid.NewGuid();
        var suppressedByFailedId = Guid.NewGuid();
        var suppressedBySucceededId = Guid.NewGuid();
        var firstEligibleId = Guid.NewGuid();
        var secondEligibleId = Guid.NewGuid();
        var payments = new List<PaymentEntity>();

        AddAttempt(
            payments,
            PaymentReferenceType.BOOKING,
            suppressedByFailedId,
            userId,
            PaymentStatus.PENDING_REDIRECT,
            paymentId: Guid.Parse("10000000-0000-0000-0000-000000000001"));
        AddAttempt(
            payments,
            PaymentReferenceType.BOOKING,
            suppressedByFailedId,
            userId,
            PaymentStatus.FAILED,
            paymentId: Guid.Parse("f0000000-0000-0000-0000-000000000001"));
        AddAttempt(payments, PaymentReferenceType.PARCEL, suppressedBySucceededId, userId, PaymentStatus.PENDING_REDIRECT);
        AddAttempt(payments, PaymentReferenceType.PARCEL, suppressedBySucceededId, userId, PaymentStatus.SUCCEEDED);
        AddAttempt(payments, PaymentReferenceType.BOOKING, firstEligibleId, userId, PaymentStatus.PENDING_REDIRECT, 111_000);
        AddAttempt(payments, PaymentReferenceType.PARCEL_ADDITIONAL, secondEligibleId, userId, PaymentStatus.PENDING_REDIRECT, 222_000);

        try
        {
            db.Payments.AddRange(payments);
            await db.SaveChangesAsync();
            await SetAttemptTimesAsync(db, payments);
            db.ChangeTracker.Clear();
            interceptor.Reset();

            var result = await mediator.Send(new LookupRedirectSessionsQuery(
                userId,
                [
                    new("PARCEL_ADDITIONAL", secondEligibleId),
                    new("BOOKING", suppressedByFailedId),
                    new("BOOKING", firstEligibleId),
                    new("PARCEL", suppressedBySucceededId),
                ]));

            result.Select(item => item.ReferenceId).Should().Equal(secondEligibleId, firstEligibleId);
            result.Select(item => item.Amount).Should().Equal(222_000, 111_000);
            interceptor.ReaderCommandCount.Should().Be(1);
            db.ChangeTracker.Entries<PaymentEntity>().Should().BeEmpty();
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM vietride_payment.payments WHERE id = ANY({payments.Select(payment => payment.Id).ToArray()})");
        }
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        CountingCommandInterceptor interceptor)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["VNPAY_BASE_URL"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FrozenClock(Now));
        services.AddVietRideDbContext<PaymentDbContext>(
            configuration,
            configureDataSource: PaymentDbContext.ConfigurePostgresTypes,
            configureDbContext: options => options.AddInterceptors(interceptor));
        services.AddVietRideMediatRBehaviors(
            handlerAssemblies: [typeof(ApplicationAssemblyMarker).Assembly]);
        services.AddInfrastructure(configuration, registerConsumers: false);
        return services.BuildServiceProvider();
    }

    private static void AddAttempt(
        ICollection<PaymentEntity> payments,
        PaymentReferenceType referenceType,
        Guid referenceId,
        Guid userId,
        PaymentStatus status,
        long amount = 100_000,
        Guid? paymentId = null)
    {
        var payment = PaymentEntity.CreatePendingRedirectVnPay(
            referenceType,
            referenceId,
            userId,
            Money.FromRaw(amount),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("N"),
            $"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef={Guid.NewGuid():N}",
            Now.AddMinutes(5));
        if (paymentId is not null)
        {
            typeof(PaymentEntity).BaseType!
                .GetProperty(nameof(PaymentEntity.Id))!
                .SetValue(payment, paymentId.Value);
        }
        PaymentContextV1 context = referenceType == PaymentReferenceType.BOOKING_GROUP
            ? new(1,
            [
                Allocation(PaymentReferenceType.BOOKING, Guid.NewGuid(), amount / 2),
                Allocation(PaymentReferenceType.BOOKING, Guid.NewGuid(), amount - (amount / 2)),
            ])
            : new(1, [Allocation(referenceType, referenceId, amount)]);
        payment.AttachContext(PaymentContextCodec.ValidateAndSerialize(
            context,
            referenceType.ToString(),
            referenceId,
            amount));
        if (status == PaymentStatus.FAILED)
        {
            payment.MarkFailed("24", Now);
        }
        else if (status == PaymentStatus.SUCCEEDED)
        {
            payment.MarkSucceeded("00", Now);
        }

        payments.Add(payment);
    }

    private static PaymentAllocationV1 Allocation(
        PaymentReferenceType referenceType,
        Guid referenceId,
        long amount)
        => new(referenceId, referenceType.ToString(), Guid.NewGuid(), Guid.NewGuid(), amount, 0, 0);

    private static async Task SetAttemptTimesAsync(PaymentDbContext db, IReadOnlyList<PaymentEntity> payments)
    {
        for (var index = 0; index < payments.Count; index++)
        {
            var createdAt = index <= 1 ? Now : Now.AddMinutes(index);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE vietride_payment.payments SET created_at = {createdAt}, updated_at = {createdAt} WHERE id = {payments[index].Id}");
        }
    }

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCommandCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }

        public void Reset() => ReaderCommandCount = 0;
    }
}
