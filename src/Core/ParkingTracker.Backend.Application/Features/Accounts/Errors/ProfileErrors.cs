using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of reading a stored profile. One error for every cause of a refused decryption (unknown key, other account,
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
	#endregion
}
