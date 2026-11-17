namespace ParkingTracker.Domain.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A closed range of house-number ordering keys, as listed on one side of a street.
/// </summary>
/// <param name="Low">The first key in the range (see <see cref="HouseNumber.LowKey"/>).</param>
/// <param name="High">The last key in the range; <see cref="long.MaxValue"/> for "to the end of the street".</param>
public sealed record HouseRange(long Low, long High)
{
	#region Public Methods
	/// <summary>
	/// Determines whether a house number falls in the range.
	/// </summary>
	/// <param name="house">The house number.</param>
	/// <returns><see langword="true"/> when its low key lies between <see cref="Low"/> and <see cref="High"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="house"/> is <see langword="null"/>.</exception>
	public bool Contains(HouseNumber house)
	{
		ArgumentNullException.ThrowIfNull(house);
		var key = house.LowKey;
		return key >= Low && key <= High;
	}
	#endregion
}
