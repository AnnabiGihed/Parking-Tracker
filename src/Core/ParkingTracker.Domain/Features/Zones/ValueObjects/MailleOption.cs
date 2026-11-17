namespace ParkingTracker.Domain.Features.Zones.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One sector area (maille) the user can tick on the card form: its code and French name, without its shape, so
///              that the list stays small.
/// </summary>
/// <param name="ZoneCode">The zone code printed on the card.</param>
/// <param name="NameFrench">The French name, or <see langword="null"/> when the service has none.</param>
public sealed record MailleOption(string ZoneCode, string? NameFrench)
{
	#region Properties
	/// <summary>
	/// Gets the name to show: the French name, or the zone code when there is none.
	/// </summary>
	public string DisplayName => string.IsNullOrEmpty(NameFrench) ? ZoneCode : NameFrench;
	#endregion
}
