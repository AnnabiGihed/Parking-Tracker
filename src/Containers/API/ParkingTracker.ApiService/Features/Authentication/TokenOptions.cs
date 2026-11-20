namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The settings of the access tokens the API issues and accepts (ADR-0014): the issuer and the two ES256 signing keys.
///              The current key signs and validates; the previous one only validates, for as long as tokens signed with it can
///              live after a rotation. The keys are secrets, mounted as files named after the configuration keys.
/// </summary>
public sealed class TokenOptions
{
	#region Constants
	/// <summary>
	/// The configuration section of the settings.
	/// </summary>
	public const string SectionName = "Tokens";

	/// <summary>
	/// The audience of every access token, and the only one the API accepts.
	/// </summary>
	public const string Audience = "parkingtracker-api";
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets the issuer of the access tokens, the base address of the API.
	/// </summary>
	public string Issuer { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the signing keys.
	/// </summary>
	public SigningKeyOptions SigningKeys { get; set; } = new();
	#endregion
}
