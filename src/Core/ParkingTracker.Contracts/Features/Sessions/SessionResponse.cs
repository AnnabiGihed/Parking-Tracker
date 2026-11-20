namespace ParkingTracker.Contracts.Features.Sessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer to a successful sign-in: the API's own short-lived access token and the single-use refresh token of the
///              device's session. The refresh token is shown once; the server keeps only its hash.
/// </summary>
/// <param name="AccessToken">The signed access token (JWT, ES256) to send as a bearer token.</param>
/// <param name="AccessTokenExpiresAt">The instant the access token expires.</param>
/// <param name="RefreshToken">The refresh token, 32 random bytes in base64url.</param>
public sealed record SessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
