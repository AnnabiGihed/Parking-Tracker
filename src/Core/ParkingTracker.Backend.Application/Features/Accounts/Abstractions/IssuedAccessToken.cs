namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : An access token the API issued, with the instant it expires.
/// </summary>
/// <param name="Token">The signed token.</param>
/// <param name="ExpiresAt">The instant the token expires.</param>
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);
