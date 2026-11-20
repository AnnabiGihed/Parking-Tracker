namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The settings of the accounts: the secret key of the subject hash. It is never rotated, because a new key would
///              orphan every account and the erasure log; the owner keeps a copy.
/// </summary>
public sealed class AccountOptions
{
	#region Constants
	/// <summary>
	/// The configuration section of the settings.
	/// </summary>
	public const string SectionName = "Accounts";

	/// <summary>
	/// The size of the subject hash key in bytes.
	/// </summary>
	public const int SubjectHashKeyBytes = 32;
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets the subject hash key as base64 of 32 bytes; empty only in Development, where a key is made at start.
	/// </summary>
	public string? SubjectHashKey { get; set; }
	#endregion
}
