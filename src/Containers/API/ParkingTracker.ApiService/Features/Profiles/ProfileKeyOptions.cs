namespace ParkingTracker.ApiService.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The two profile keys, each base64 of 32 bytes.
/// </summary>
public sealed class ProfileKeyOptions
{
	#region Properties
	/// <summary>
	/// Gets or sets the current key; empty only in Development, where a key is made at start.
	/// </summary>
	public string? Current { get; set; }

	/// <summary>
	/// Gets or sets the key of the rotation before, used to decrypt only; empty when there is none.
	/// </summary>
	public string? Previous { get; set; }
	#endregion
}
