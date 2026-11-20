using Pivot.Framework.Application.Abstractions.Messaging.Commands;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.EraseAccount;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks to erase the own account: the account and all its sessions are deleted and the subject hash goes to the erasure
///              log, in one transaction. Erasing an account that is gone succeeds.
/// </summary>
/// <param name="AccountId">The identifier of the account, from the subject of the access token.</param>
public sealed record EraseAccountCommand(Guid AccountId) : ICommand;
