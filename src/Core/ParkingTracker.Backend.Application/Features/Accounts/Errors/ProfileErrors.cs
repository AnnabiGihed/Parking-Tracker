using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of reading and writing a stored profile. One error for every cause of a refused decryption (unknown key, other account,
///              other revision, changed bytes), so nothing tells which check failed; the unknown key is separate because it is an
///              operator mistake (a lost key) and not a sign of tampering.
/// </summary>
public static class ProfileErrors
{
	#region Static Instances
	/// <summary>
	/// The profile cannot be read: it is not the document that was stored for this account and revision.
	/// </summary>
	public static readonly Error Unreadable = new("Profile.Unreadable", "The stored profile cannot be read.");

	/// <summary>
	/// The key that encrypted the profile is neither the current nor the previous key.
	/// </summary>
	public static readonly Error KeyUnknown = new("Profile.KeyUnknown", "The key that encrypted the stored profile is not configured.");

	/// <summary>
	/// The account has no profile yet.
	/// </summary>
	public static readonly Error NotFound = new("Profile.NotFound", "The account has no profile yet.");

	/// <summary>
	/// The write is not based on what is stored: a create found a profile, a replacement names a revision that is not the stored one
	/// or finds none, or another write won the save. The caller reads the profile again before it writes.
	/// </summary>
	public static readonly Error PreconditionFailed = new("Profile.PreconditionFailed", "The stored profile is not the one the write is based on.");

	/// <summary>
	/// The document, as it is stored, is larger than a profile may be.
	/// </summary>
	public static readonly Error TooLarge = new("Profile.TooLarge", "The profile document is too large.");
	#endregion
}
