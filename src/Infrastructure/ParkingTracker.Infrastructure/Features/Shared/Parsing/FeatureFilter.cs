namespace ParkingTracker.Infrastructure.Features.Shared.Parsing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Which features of a collection are handed to the converter.
/// </summary>
internal enum FeatureFilter
{
	/// <summary>
	/// Every feature, with or without a geometry.
	/// </summary>
	All,

	/// <summary>
	/// Only the features that have a geometry.
	/// </summary>
	WithGeometry,
}
