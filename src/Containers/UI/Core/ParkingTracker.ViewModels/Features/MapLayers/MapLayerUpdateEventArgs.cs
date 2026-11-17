namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Event data of a map layer that has new content (or none, to clear it).
/// </summary>
public sealed class MapLayerUpdateEventArgs : EventArgs
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MapLayerUpdateEventArgs"/> class.
	/// </summary>
	/// <param name="layer">The name of the layer, for example <see cref="MapLayersViewModel.StreetsLayer"/>.</param>
	/// <param name="surfaces">The areas of the layer.</param>
	/// <param name="points">The markers of the layer.</param>
	public MapLayerUpdateEventArgs(string layer, IReadOnlyList<MapSurface> surfaces, IReadOnlyList<MapPoint> points)
	{
		Layer = layer;
		Surfaces = surfaces;
		Points = points;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the name of the layer.
	/// </summary>
	public string Layer { get; }

	/// <summary>
	/// Gets the areas of the layer; empty to clear.
	/// </summary>
	public IReadOnlyList<MapSurface> Surfaces { get; }

	/// <summary>
	/// Gets the markers of the layer; empty to clear.
	/// </summary>
	public IReadOnlyList<MapPoint> Points { get; }
	#endregion
}
