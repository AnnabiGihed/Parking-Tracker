namespace ParkingTracker.ApiService.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The settings of the profile encryption (ADR-0032): the current key, which encrypts and decrypts, and the previous
///              key of a rotation, which only decrypts. Both are secrets, mounted as files named after the configuration keys.
///              Losing the current key makes every stored profile unreadable; the owner keeps a copy.
/// </summary>
public sealed class ProfileOptions
{
	#region Constants
	/// <summary>
	/// The configuration section of the settings.
	/// </summary>
	public const string SectionName = "Profiles";

	/// <summary>
	/// The size of a profile key in bytes (AES-256).
	/// </summary>
	public const int KeyBytes = 32;
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets the profile keys.
	/// </summary>
	public ProfileKeyOptions Keys { get; set; } = new();
	#endregion
}
