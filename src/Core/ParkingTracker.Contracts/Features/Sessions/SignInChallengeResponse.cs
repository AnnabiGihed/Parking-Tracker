namespace ParkingTracker.Contracts.Features.Sessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer to a sign-in challenge request: the one-time nonce the app hands to Google so that the ID token it gets
///              back can be used once only.
/// </summary>
/// <param name="Nonce">The random single-use nonce, 32 random bytes in base64url.</param>
/// <param name="ExpiresAt">The instant after which the nonce is refused.</param>
public sealed record SignInChallengeResponse(string Nonce, DateTimeOffset ExpiresAt);
