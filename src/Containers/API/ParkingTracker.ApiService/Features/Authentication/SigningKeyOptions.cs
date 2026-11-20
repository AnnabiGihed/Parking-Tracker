namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The two signing keys of the access tokens, each a P-256 private key in PEM text.
/// </summary>
public sealed class SigningKeyOptions
{
	#region Properties
	/// <summary>
	/// Gets or sets the key that signs new tokens and validates tokens; empty only in Development, where a key is made at start.
	/// </summary>
	public string? Current { get; set; }

	/// <summary>
	/// Gets or sets the key of the rotation before, which only validates; empty when there is none.
	/// </summary>
	public string? Previous { get; set; }
	#endregion
}
