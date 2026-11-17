using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.Zones.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A roadworks site or traffic event that is running now, with where it is.
/// </summary>
/// <param name="Kind">Roadworks or event.</param>
/// <param name="Points">The points of a point-shaped item, or <see langword="null"/> when the item has a surface.</param>
/// <param name="Surface">The surface of an area-shaped item, or <see langword="null"/> when the item has points.</param>
/// <param name="Title">What it is, for example the nature of the works.</param>
/// <param name="Where">Where it is, in words; empty when unknown.</param>
/// <param name="To">The last day as <c>yyyy-MM-dd</c>, or <see langword="null"/>.</param>
/// <param name="From">The first day as <c>yyyy-MM-dd</c>, or <see langword="null"/>; shown on the map only.</param>
/// <param name="By">Who runs the works or what the event changes; empty when unknown; shown on the map only.</param>
/// <param name="Link">Address of a page with details, or empty; shown on the map only.</param>
public sealed record WorkItem(WorkKind Kind, IReadOnlyList<GeoPoint>? Points, SurfaceShape? Surface, string Title, string Where, string? To, string? From = null, string By = "", string Link = "")
{
	#region Constants
	/// <summary>
	/// How far from a point-shaped item a position still counts as at it, in metres.
	/// </summary>
	public const double PointRadiusMetres = 25;

	/// <summary>
	/// How far from the surface of an area-shaped item a position still counts as at it, in metres.
	/// </summary>
	public const double SurfaceRadiusMetres = 5;
	#endregion

	#region Public Methods
	/// <summary>
	/// Determines whether a position is at this item: within 25 m of one of its points or within 5 m of its surface.
	/// </summary>
	/// <param name="position">The position to test.</param>
	/// <returns><see langword="true"/> when the position is at the item.</returns>
	public bool IsAt(GeoPoint position)
	{
		return Points is not null
			? Points.Any(point => GeoMath.Distance(position, point) <= PointRadiusMetres)
			: Surface is not null && Surface.ClosestTo(position).Metres <= SurfaceRadiusMetres;
	}
	#endregion
}
