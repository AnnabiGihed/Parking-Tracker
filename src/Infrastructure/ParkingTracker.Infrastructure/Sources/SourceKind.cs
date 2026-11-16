namespace ParkingTracker.Infrastructure.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The live services the app reads, used to name the failure codes of a source.
/// </summary>
internal enum SourceKind
{
	/// <summary>
	/// The parking.brussels WFS service.
	/// </summary>
	ParkingBrussels,

	/// <summary>
	/// The UrbIS address points.
	/// </summary>
	Urbis,

	/// <summary>
	/// The Brussels Mobility parking rules API.
	/// </summary>
	ParkingRules,

	/// <summary>
	/// The Brussels Mobility roadworks and events layers.
	/// </summary>
	Mobility,

	/// <summary>
	/// The City of Brussels website.
	/// </summary>
	City,
}
