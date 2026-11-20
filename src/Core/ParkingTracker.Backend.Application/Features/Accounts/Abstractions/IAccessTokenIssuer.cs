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
	/// Issues an access token whose subject is the account identifier.
	/// </summary>
	/// <param name="accountId">The account the token is for.</param>
	/// <param name="now">The current moment, the start of the token's lifetime.</param>
	/// <returns>The signed token and its expiry.</returns>
	IssuedAccessToken Issue(UserAccountId accountId, DateTimeOffset now);
	#endregion
}
