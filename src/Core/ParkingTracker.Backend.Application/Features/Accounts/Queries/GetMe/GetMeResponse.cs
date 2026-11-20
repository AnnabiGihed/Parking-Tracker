namespace ParkingTracker.Backend.Application.Features.Accounts.Queries.GetMe;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The account as the server knows it: identifier, two dates and the number of devices signed in.
/// </summary>
/// <param name="AccountId">The identifier of the account.</param>
/// <param name="CreatedOn">The day the account was created.</param>
/// <param name="LastSeenOn">The last day the user signed in or refreshed.</param>
/// <param name="ActiveSessionCount">The number of devices the account is signed in on.</param>
public sealed record GetMeResponse(Guid AccountId, DateOnly CreatedOn, DateOnly LastSeenOn, int ActiveSessionCount);
