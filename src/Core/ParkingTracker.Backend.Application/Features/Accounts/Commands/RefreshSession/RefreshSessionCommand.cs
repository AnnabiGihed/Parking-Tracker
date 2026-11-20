using Pivot.Framework.Application.Abstractions.Messaging.Commands;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.RefreshSession;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks to exchange a refresh token for a new access token and the next refresh token of its family. The command has no
///              validator on purpose; every refusal, an empty token included, is the same generic failure.
/// </summary>
/// <param name="RefreshToken">The refresh token as the app sent it.</param>
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<RefreshSessionResponse>;
