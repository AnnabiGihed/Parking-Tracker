namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The area the map shows, in degrees, as the map reports it after every move.
/// </summary>
/// <param name="South">The southern edge in degrees of latitude.</param>
/// <param name="West">The western edge in degrees of longitude.</param>
/// <param name="North">The northern edge in degrees of latitude.</param>
/// <param name="East">The eastern edge in degrees of longitude.</param>
public sealed record MapBounds(double South, double West, double North, double East)
{
	#region Public Methods
	/// <summary>
	/// Grows the area on every side by a share of its height and width, so that the layers are loaded a little beyond the screen.
	/// </summary>
	/// <param name="ratio">The share of the height and width to add on each side, for example <c>0.1</c>.</param>
	/// <returns>The larger area.</returns>
	public MapBounds Pad(double ratio)
	{
		var height = Math.Abs(North - South) * ratio;
		var width = Math.Abs(East - West) * ratio;
		return new MapBounds(South - height, West - width, North + height, East + width);
	}
	#endregion
}
