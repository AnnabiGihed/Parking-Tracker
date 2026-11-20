using Pivot.Framework.Application.Abstractions.Messaging.Commands;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.ExchangeGoogleIdToken;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks to sign in: exchanges a Google ID token for an access token and a refresh token of a new device session. The
///              command has no validator on purpose; every refusal, an empty token included, is the same generic failure.
/// </summary>
/// <param name="IdToken">The Google ID token as the app sent it.</param>
public sealed record ExchangeGoogleIdTokenCommand(string IdToken) : ICommand<ExchangeGoogleIdTokenResponse>;
