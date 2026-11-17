namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : How a chip under the status block looks.
/// </summary>
public enum ChipKind
{
	/// <summary>
	/// A grey chip with an icon.
	/// </summary>
	Plain,

	/// <summary>
	/// A green chip with a pulsing icon: the answer comes from live data.
	/// </summary>
	Live,

	/// <summary>
	/// A chip with a coloured dot for the zone.
	/// </summary>
	Zone,

	/// <summary>
	/// A chip with a spinner: a check is running.
	/// </summary>
	Busy,
}
