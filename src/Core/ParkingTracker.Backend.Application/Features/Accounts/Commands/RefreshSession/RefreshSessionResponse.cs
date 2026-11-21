namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.RefreshSession;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The tokens of a successful refresh. The new refresh token exists in clear text only here and in the answer; the
///              server keeps its hash, and the token that was presented is spent.
/// </summary>
/// <param name="AccessToken">The signed access token.</param>
/// <param name="AccessTokenExpiresAt">The instant the access token expires.</param>
/// <param name="RefreshToken">The next refresh token, 32 random bytes in base64url.</param>
public sealed record RefreshSessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
