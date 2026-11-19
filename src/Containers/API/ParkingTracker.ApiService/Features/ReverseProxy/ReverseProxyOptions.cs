namespace ParkingTracker.ApiService.Features.ReverseProxy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The settings of the trust in the shared reverse proxy (ADR-0029). The key is the secret the proxy adds to every request
///              it forwards; the server mounts it as a secret file named after the configuration key.
/// </summary>
public sealed class ReverseProxyOptions
{
	#region Constants
	/// <summary>
	/// The configuration section of the settings.
	/// </summary>
	public const string SectionName = "ReverseProxy";
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets the proxy key; empty or <see langword="null"/> when none is configured.
	/// </summary>
	public string? Key { get; set; }
	#endregion
}
