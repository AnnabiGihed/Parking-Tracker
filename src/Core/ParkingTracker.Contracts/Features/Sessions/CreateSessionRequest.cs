namespace ParkingTracker.Contracts.Features.Sessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The body of the sign-in request: the Google ID token that the app got from Android Credential Manager, bound to a
///              nonce from a sign-in challenge.
/// </summary>
/// <param name="IdToken">The Google ID token, a signed JWT.</param>
public sealed record CreateSessionRequest(string IdToken);
