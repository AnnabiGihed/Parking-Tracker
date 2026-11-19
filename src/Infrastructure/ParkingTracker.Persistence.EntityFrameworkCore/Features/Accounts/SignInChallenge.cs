namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A nonce the API issued for one sign-in, kept in the database so that a deployment during a sign-in loses none. It
///              is a row of the persistence layer, not a domain concept: a nonce is consumed once with a single statement.
/// </summary>
public sealed class SignInChallenge
{
	#region Properties
	/// <summary>
	/// Gets the random, single-use nonce.
	/// </summary>
	public required string Nonce { get; init; }

	/// <summary>
	/// Gets the instant after which the nonce is refused.
	/// </summary>
	public required DateTimeOffset ExpiresAtUtc { get; init; }
	#endregion
}
