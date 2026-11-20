namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.CreateSignInChallenge;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The nonce a sign-in challenge issued and the instant it stops being accepted.
/// </summary>
/// <param name="Nonce">The random single-use nonce.</param>
/// <param name="ExpiresAt">The instant after which the nonce is refused.</param>
public sealed record CreateSignInChallengeResponse(string Nonce, DateTimeOffset ExpiresAt);
