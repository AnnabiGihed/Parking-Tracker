using ParkingTracker.Contracts.Features.Profiles;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks to store the profile document of the own account, encrypted, under a condition on what is stored (ADR-0032).
/// </summary>
/// <param name="AccountId">The identifier of the account, from the subject of the access token.</param>
/// <param name="Document">The profile document, validated before it is stored.</param>
/// <param name="Condition">Create the first profile, or replace one named revision.</param>
public sealed record SaveProfileCommand(Guid AccountId, ProfileDocument Document, ProfileWriteCondition Condition) : ICommand<SaveProfileResponse>;
