using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Auth.Refresh;

public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, TokenBundleDto>
{
    private const int AccessTokenTtlSeconds = 900; // 15 minutes
    private readonly IRefreshSessionExecutor _refreshSessionExecutor;
    private readonly IAccessTokenService _accessTokenService;

    public RefreshCommandHandler(
        IRefreshSessionExecutor refreshSessionExecutor,
        IAccessTokenService accessTokenService)
    {
        _refreshSessionExecutor = refreshSessionExecutor;
        _accessTokenService = accessTokenService;
    }

    public async Task<TokenBundleDto> Handle(
        RefreshCommand request,
        CancellationToken cancellationToken)
    {
        var outcome = await _refreshSessionExecutor.ExecuteAsync(request.RefreshToken, cancellationToken);
        if (!outcome.IsSuccess)
        {
            if (outcome.FailureCode is "AUTH_ACCOUNT_LOCKED" or "OPERATOR_SUSPENDED" or "FORBIDDEN")
            {
                throw new ForbiddenException(
                    outcome.FailureCode,
                    outcome.FailureMessage ?? "Access is forbidden for the current operator status.");
            }

            throw new UnauthorizedException("AUTH_TOKEN_INVALID", outcome.FailureMessage ?? "Refresh token is invalid.");
        }

        var user = outcome.User!;
        var accessToken = _accessTokenService.IssueToken(user, outcome.OperatorStatus);

        return new TokenBundleDto(
            AccessToken: accessToken,
            RefreshToken: outcome.RefreshToken!,
            ExpiresInSeconds: AccessTokenTtlSeconds,
            User: new UserSummaryDto(
                Id: user.Id,
                Email: user.Email,
                Phone: user.Phone?.Value,
                DisplayName: user.DisplayName,
                Role: user.Role.ToString(),
                OperatorId: user.OperatorId,
                Status: user.Status.ToString(),
                OperatorRegistrationStatus: outcome.OperatorStatus?.ToString()));
    }
}
