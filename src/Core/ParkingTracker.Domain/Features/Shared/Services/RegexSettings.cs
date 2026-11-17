namespace ParkingTracker.Domain.Features.Shared.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Settings shared by every regular expression of the domain, so a pathological text cannot hang the app.
/// </summary>
internal static class RegexSettings
{
	#region Static Instances
	/// <summary>
	/// The longest a single match may take before it is abandoned with a timeout.
	/// </summary>
	internal static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
	#endregion
}
