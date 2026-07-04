namespace VietRide.Identity.Application.Abstractions;

public interface IInitialPasswordTokenService
{
    string GenerateCode();

    DateTimeOffset GetExpiresAt(DateTimeOffset now);

    string BuildSetInitialPasswordUrl(string code);
}
