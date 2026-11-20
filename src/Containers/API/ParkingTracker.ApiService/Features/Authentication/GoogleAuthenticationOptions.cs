namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The settings for checking a Google ID token (ADR-0014): where Google publishes its signing keys, the issuers it signs
///              with and the client identifiers a token may be meant for. Outside Development the address and the issuers must be
///              the ones of Google, so a test issuer cannot be switched on in a deployed environment (ADR-0025); only the audiences
///              are configuration of the project (appsettings.Production.json).
/// </summary>
public sealed class GoogleAuthenticationOptions
{
	#region Constants
	/// <summary>
	/// The configuration section of the settings.
	/// </summary>
	public const string SectionName = "Authentication:Google";

	/// <summary>
	/// The address of the OpenID Connect metadata of Google, which links to its signing keys.
	/// </summary>
	public const string GoogleMetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

	/// <summary>
	/// The issuer of Google without the scheme.
	/// </summary>
	public const string GoogleIssuer = "accounts.google.com";

	/// <summary>
	/// The issuer of Google with the scheme.
	/// </summary>
	public const string GoogleIssuerWithScheme = "https://accounts.google.com";
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets the address of the OpenID Connect metadata.
	/// </summary>
	public string MetadataAddress { get; set; } = GoogleMetadataAddress;

	/// <summary>
	/// Gets or sets the issuers a token may carry.
	/// </summary>
	public IList<string> ValidIssuers { get; set; } = [GoogleIssuer, GoogleIssuerWithScheme];

	/// <summary>
	/// Gets or sets the client identifiers a token may be meant for, the Web client of the project.
	/// </summary>
	public IList<string> Audiences { get; set; } = [];
	#endregion
}
