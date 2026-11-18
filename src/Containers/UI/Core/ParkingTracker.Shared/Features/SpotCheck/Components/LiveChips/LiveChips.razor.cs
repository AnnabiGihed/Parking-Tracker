using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.Shared.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The chips under the status block: when the answer was read (green and pulsing when it comes from live data),
///              where the position came from, and the zone with its colour.
/// </summary>
public sealed partial class LiveChips : ViewModelComponent<SpotCheckViewModel>
{
	#region Private Helpers
	/// <summary>
	/// Gets the CSS classes of a chip.
	/// </summary>
	/// <param name="chip">The chip.</param>
	/// <returns>The classes: <c>chip</c>, which the tests and the styles read, and the kind of the chip.</returns>
	private static string ChipClass(StatusChip chip) => chip.Kind switch
	{
		ChipKind.Live => "chip live",
		ChipKind.Zone => "chip zone-chip",
		_ => "chip",
	};
	#endregion
}
