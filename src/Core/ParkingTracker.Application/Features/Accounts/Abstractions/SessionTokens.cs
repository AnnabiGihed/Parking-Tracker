namespace ParkingTracker.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The two tokens of a signed-in device (ADR-0015): the access token the API accepts until it expires, and the single-use
///              refresh token that trades for new ones. Both are credentials, so <see cref="ToString"/> shows no value, which keeps them out of logs and failure messages.
/// </summary>
/// <param name="AccessToken">The short-lived bearer token.</param>
/// <param name="AccessTokenExpiresAt">The moment the access token stops working.</param>
/// <param name="RefreshToken">The refresh token, valid once.</param>
public sealed record SessionTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken)
{
	#region Overrides
	/// <inheritdoc />
	public override string ToString() => nameof(SessionTokens);
	#endregion
}
