using VietRide.Parcel.Application.Features.Parcels.Quotes;

namespace VietRide.Parcel.Application.Abstractions.Security;

public interface IParcelQuoteTokenService
{
    int TtlSeconds { get; }

    string Issue(ParcelQuoteTokenPayload payload);

    ParcelQuoteTokenReadOutcome Read(string token, DateTimeOffset now);
}

public enum ParcelQuoteTokenReadOutcomeKind
{
    Success,
    Invalid,
    Expired,
}

public sealed record ParcelQuoteTokenReadOutcome(
    ParcelQuoteTokenReadOutcomeKind Kind,
    ParcelQuoteTokenPayload? Payload);
