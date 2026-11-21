using Pivot.Framework.Application.Abstractions.Messaging.Commands;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.EndSession;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks to sign out the device the caller holds: revokes the family of the device session named in the access token.
///              Signing out twice, or out of a family that is gone, succeeds.
/// </summary>
/// <param name="AccountId">The identifier of the account, from the subject of the access token.</param>
/// <param name="FamilyId">The family of the device session, from the session claim of the access token.</param>
public sealed record EndSessionCommand(Guid AccountId, Guid FamilyId) : ICommand;
