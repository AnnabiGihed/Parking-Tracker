namespace ParkingTracker.Domain.Features.Zones.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The English names of the parking.brussels zone colour codes, as shown to the user.
/// </summary>
public static class ZoneNames
{
	#region Static Instances
	/// <summary>
	/// The name of each zone code.
	/// </summary>
	private static readonly Dictionary<int, string> Names = new()
	{
		[0] = "unregulated",
		[1] = "red",
		[2] = "orange",
		[3] = "grey",
		[4] = "green",
		[5] = "blue",
		[6] = "event",
		[9] = "residents only",
	};
	#endregion

	#region Public Methods
	/// <summary>
	/// Gives the name of a colour code.
	/// </summary>
	/// <param name="colourCode">The <c>typreg</c> colour code, or <see langword="null"/>.</param>
	/// <returns>The name, or <see langword="null"/> when the code is missing or unknown.</returns>
	public static string? Of(int? colourCode)
	{
		return colourCode is { } code && Names.TryGetValue(code, out var name) ? name : null;
	}
	#endregion
}
