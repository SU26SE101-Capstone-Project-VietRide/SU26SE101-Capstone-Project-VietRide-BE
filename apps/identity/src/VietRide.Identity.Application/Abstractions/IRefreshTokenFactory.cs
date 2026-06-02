using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Creates opaque refresh tokens and their corresponding domain entity.
/// The raw token is returned to the caller exactly once and is never persisted.
/// Only the deterministic SHA-256 hex hash is stored in <c>refresh_tokens.token_hash</c>.
/// </summary>
public interface IRefreshTokenFactory
{
    /// <summary>
    /// Creates a new refresh token.
    /// </summary>
    /// <param name="userId">Owner of the token.</param>
    /// <param name="parentTokenId">
    ///   The ID of the token being rotated (null for the first token in a family).
    /// </param>
    /// <param name="familyId">
    ///   The rotation chain identifier. Pass <c>null</c> to start a new family
    ///   (a new <see cref="Guid"/> will be generated).
    /// </param>
    /// <returns>
    ///   A tuple of:
    ///   <list type="bullet">
    ///     <item><c>rawToken</c> — 32 hex chars to send to the client, never stored.</item>
    ///     <item><c>entity</c> — the domain entity whose <c>TokenHash</c> is the SHA-256
    ///     hex of the raw token; persist this entity, not the raw token.</item>
    ///   </list>
    /// </returns>
    (string rawToken, RefreshToken entity) Create(
        Guid userId,
        Guid? parentTokenId,
        Guid? familyId);

    /// <summary>
    /// Computes the deterministic SHA-256 hex hash of a raw token for DB lookup.
    /// Same algorithm as used when storing the token hash — enables the
    /// <c>uq_refresh_tokens_token_hash</c> index lookup.
    /// </summary>
    string ComputeHash(string rawToken);
}
