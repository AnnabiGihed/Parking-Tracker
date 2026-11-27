namespace ParkingTracker.ViewModels.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The one place that says whether the data on the phone is known to belong to the signed-in account (ADR-0032). The account
///              flow sets the hash of the account it got from the API in this run when it equals the hash bound on the phone, and clears
///              it whenever that is no longer true; the profile sync reads it and never uploads while it is empty. It lives apart from
///              the view model so that the sync does not depend on a screen.
/// </summary>
public sealed class OwnerVerification
{
	#region Fields
	/// <summary>
	/// The hash of the verified account, or <see langword="null"/>; replaced as a whole, so a read never sees half a write.
	/// </summary>
	private volatile string? verifiedHash;
	#endregion

	#region Properties
	/// <summary>
	/// Gets the hash of the account whose ownership of the data on the phone was verified in this run, or <see langword="null"/>.
	/// </summary>
	public string? VerifiedHash => verifiedHash;

	/// <summary>
	/// Gets a value indicating whether the ownership of the data was verified.
	/// </summary>
	public bool IsVerified => verifiedHash is not null;
	#endregion

	#region Public Methods
	/// <summary>
	/// Records that the data on the phone belongs to the account with this hash.
	/// </summary>
	/// <param name="accountHash">The hash of the account, from <c>DeviceOwnerHash.Of</c>.</param>
	/// <exception cref="ArgumentException"><paramref name="accountHash"/> is empty.</exception>
	public void Verify(string accountHash)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(accountHash);
		verifiedHash = accountHash;
	}

	/// <summary>
	/// Forgets the verification, as after a sign-out or when another account's data is on the phone.
	/// </summary>
	public void Clear() => verifiedHash = null;
	#endregion
}
