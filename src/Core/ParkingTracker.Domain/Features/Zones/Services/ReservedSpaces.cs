namespace ParkingTracker.Domain.Features.Zones.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The reservation types of on-street parking spaces (parking.brussels <c>typres</c>) and their names for the user.
/// </summary>
public static class ReservedSpaces
{
	#region Static Instances
	/// <summary>
	/// The label of each reservation type.
	/// </summary>
	private static readonly Dictionary<int, string> Labels = new()
	{
		[1] = "🚚 Delivery space",
		[2] = "🚛 Trucks only",
		[3] = "🚌 Bus only",
		[5] = "♿ Disabled (PMR) space",
		[6] = "🚕 Taxi rank",
		[8] = "🏛️ Diplomatic (CD) space",
		[9] = "🚓 Police only",
		[10] = "🏍️ Motorbikes only",
		[11] = "🚲 Bicycles only",
		[12] = "👋 Kiss & ride",
		[13] = "🚚 Delivery zone",
		[14] = "👋 Kiss & ride zone",
		[16] = "⏱️ 30-minute zone",
		[17] = "⏱️ 60-minute zone",
		[18] = "🛍️ Shop & go zone",
		[19] = "⚡ EV charging zone",
		[20] = "🚗 Car-sharing zone",
		[21] = "🚌 Coach drop zone",
		[22] = "🚌 Coach waiting zone",
		[23] = "🚌 Coach overnight zone",
		[24] = "🚛 Truck zone",
		[25] = "🚛 Zone E1",
		[26] = "⛔ Other reserved space",
	};

	/// <summary>
	/// The reservation types that are limited in time.
	/// </summary>
	private static readonly HashSet<int> TimeLimitedTypes = [16, 17, 18];
	#endregion

	#region Public Methods
	/// <summary>
	/// Gives the name of a reservation type.
	/// </summary>
	/// <param name="reservationType">The <c>typres</c> value; 0 is an ordinary space.</param>
	/// <returns>The name, or <see langword="null"/> when the type is unknown.</returns>
	public static string? LabelOf(int reservationType)
	{
		return Labels.GetValueOrDefault(reservationType);
	}

	/// <summary>
	/// Tells whether a reservation type is a time-limited zone (30 minutes, 60 minutes, shop and go), for which a residents' card does not extend the time.
	/// </summary>
	/// <param name="reservationType">The <c>typres</c> value.</param>
	/// <returns><see langword="true"/> for the time-limited types.</returns>
	public static bool IsTimeLimited(int reservationType) => TimeLimitedTypes.Contains(reservationType);
	#endregion
}
