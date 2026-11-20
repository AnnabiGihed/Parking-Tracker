using Pivot.Framework.Application.Abstractions.Messaging.Commands;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.CreateSignInChallenge;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for a one-time nonce that a sign-in will bind its Google ID token to. It carries no input.
/// </summary>
public sealed record CreateSignInChallengeCommand : ICommand<CreateSignInChallengeResponse>;
