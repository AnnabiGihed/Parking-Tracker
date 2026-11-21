namespace ParkingTracker.Contracts.Features.Sessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The body of the refresh request: the single-use refresh token the app holds. The answer is a
///              <see cref="SessionResponse"/> with the next refresh token; the presented one is spent.
/// </summary>
/// <param name="RefreshToken">The refresh token, 32 random bytes in base64url.</param>
public sealed record RefreshSessionRequest(string RefreshToken);
