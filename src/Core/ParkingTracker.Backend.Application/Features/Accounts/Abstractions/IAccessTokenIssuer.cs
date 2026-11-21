using ParkingTracker.Domain.Features.Accounts.Aggregates;

namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that issues the API's own short-lived access token for an account (ADR-0014).
/// </summary>
public interface IAccessTokenIssuer
{
	#region Methods
	/// <summary>
	/// Issues an access token whose subject is the account identifier and whose session claim is the family of the device session, so
	/// that a sign-out can tell which device the caller holds.
	/// </summary>
	/// <param name="accountId">The account the token is for.</param>
	/// <param name="familyId">The family of the device session the token belongs to.</param>
	/// <param name="now">The current moment, the start of the token's lifetime.</param>
	/// <returns>The signed token and its expiry.</returns>
	IssuedAccessToken Issue(UserAccountId accountId, Guid familyId, DateTimeOffset now);
	#endregion
}
