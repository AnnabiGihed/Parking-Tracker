using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Backend.Application.Features.Accounts.Queries.GetProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the decrypted profile document of the own account of the signed-in user.
/// </summary>
/// <param name="AccountId">The identifier of the account, from the subject of the access token.</param>
public sealed record GetProfileQuery(Guid AccountId) : IQuery<GetProfileResponse>;
