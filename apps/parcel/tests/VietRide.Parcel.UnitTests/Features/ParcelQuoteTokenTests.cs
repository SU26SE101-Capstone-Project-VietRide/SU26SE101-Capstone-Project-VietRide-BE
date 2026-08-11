using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Security;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.Quotes;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure.Security;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelQuoteTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 10, 0, 0, TimeSpan.FromHours(7));
    private static readonly Guid SenderId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid OriginId = Guid.NewGuid();
    private static readonly Guid DestinationId = Guid.NewGuid();

    [Fact]
    public void HmacToken_ReadsValidTokenAndRejectsTamperAndExpiry()
    {
        var tokenService = CreateTokenService(ttlSeconds: 600);
        var payload = CreatePayload(Now.AddMinutes(10));
        var token = tokenService.Issue(payload);

        tokenService.Read(token, Now).Kind.Should().Be(ParcelQuoteTokenReadOutcomeKind.Success);
        tokenService.Read(token + "x", Now).Kind.Should().Be(ParcelQuoteTokenReadOutcomeKind.Invalid);
        tokenService.Read(token, payload.ExpiresAt).Kind.Should().Be(ParcelQuoteTokenReadOutcomeKind.Expired);
    }

    [Fact]
    public async Task ValidateToken_RejectsRequestMismatchAndStaleFareWithCanonicalCodes()
    {
        var fareRepository = Substitute.For<IParcelRouteFareRepository>();
        var fare = CreateFare();
        fareRepository.FindByCompositeAsync(RouteId, ParcelSizeCategory.SMALL, Arg.Any<CancellationToken>())
            .Returns(fare);
        var tokenService = CreateTokenService(ttlSeconds: 600);
        var quoteService = new ParcelQuoteService(fareRepository, tokenService: tokenService);
        var cargo = ParcelCargoCalculator.Calculate(10m, 10m, 10m, 2m, 6000m);
        var quote = quoteService.Calculate(cargo, fare, 0, 6000m);
        var issued = quoteService.IssueToken(
            quote,
            SenderId,
            TripId,
            RouteId,
            OperatorId,
            OriginId,
            DestinationId,
            Now)!;

        var valid = await quoteService.ValidateTokenAsync(
            issued.Token,
            Expectation(SenderId),
            Now.AddSeconds(1),
            CancellationToken.None);
        valid.TripId.Should().Be(TripId);

        var mismatch = () => quoteService.ValidateTokenAsync(
            issued.Token,
            Expectation(Guid.NewGuid()),
            Now.AddSeconds(1),
            CancellationToken.None);
        (await mismatch.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("PARCEL_QUOTE_MISMATCH");

        var categoryMismatch = () => quoteService.ValidateTokenAsync(
            issued.Token,
            Expectation(SenderId, ParcelSizeCategory.MEDIUM),
            Now.AddSeconds(1),
            CancellationToken.None);
        (await categoryMismatch.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("PARCEL_QUOTE_MISMATCH");

        fare.UpdateWeightPricing(Money.FromRaw(2), Money.FromRaw(150_000));
        var stale = () => quoteService.ValidateTokenAsync(
            issued.Token,
            Expectation(SenderId),
            Now.AddSeconds(1),
            CancellationToken.None);
        (await stale.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("PARCEL_QUOTE_STALE");
    }

    private static HmacParcelQuoteTokenService CreateTokenService(int ttlSeconds)
        => new(new ParcelQuoteTokenOptions(
            "parcel-quote-unit-test-secret-at-least-32-characters",
            ttlSeconds));

    private static ParcelRouteFare CreateFare()
    {
        var fare = ParcelRouteFare.Create(
            RouteId,
            ParcelSizeCategory.SMALL,
            OperatorId,
            Money.FromRaw(150_000),
            Now.AddDays(-1));
        fare.UpdateWeightPricing(Money.FromRaw(1), Money.FromRaw(150_000));
        fare.CreatedAt = Now.AddDays(-1);
        fare.UpdatedAt = Now.AddDays(-1);
        return fare;
    }

    private static ParcelQuoteTokenExpectation Expectation(
        Guid senderId,
        ParcelSizeCategory? sizeCategory = null)
        => new(
            senderId,
            TripId,
            RouteId,
            OperatorId,
            OriginId,
            DestinationId,
            10m,
            10m,
            10m,
            2m,
            sizeCategory);

    private static ParcelQuoteTokenPayload CreatePayload(DateTimeOffset expiresAt)
        => new(
            1,
            SenderId,
            TripId,
            RouteId,
            OperatorId,
            OriginId,
            DestinationId,
            10m,
            10m,
            10m,
            2m,
            0.001m,
            0.17m,
            2m,
            "SMALL",
            Guid.NewGuid(),
            Now,
            Now.AddDays(-1),
            null,
            1,
            150_000,
            6000m,
            1,
            2,
            150_000,
            0,
            20m,
            30_000,
            Now,
            expiresAt,
            Guid.NewGuid());
}
