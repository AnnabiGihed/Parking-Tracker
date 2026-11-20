namespace ParkingTracker.Contracts.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The signed-in user's own account as the server knows it: an identifier and two dates, nothing that identifies the person.
/// </summary>
/// <param name="AccountId">The identifier of the account.</param>
/// <param name="CreatedOn">The day the account was created.</param>
/// <param name="LastSeenOn">The last day the user signed in or refreshed.</param>
/// <param name="ActiveSessionCount">The number of devices the account is signed in on.</param>
public sealed record MeResponse(Guid AccountId, DateOnly CreatedOn, DateOnly LastSeenOn, int ActiveSessionCount);
