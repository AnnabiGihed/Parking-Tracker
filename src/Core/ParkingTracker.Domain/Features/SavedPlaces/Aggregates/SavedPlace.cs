using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Primitives;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.SavedPlaces.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One place the user saved: its kind, a label, a point inside the Brussels box and the address in words. It is a
///              child of <see cref="SavedPlaces"/>, which decides how many of each kind there can be.
/// </summary>
public sealed class SavedPlace : Entity<SavedPlaceId>
{
	#region Properties
	/// <summary>
	/// Gets the kind of place.
	/// </summary>
	public PlaceKind Kind { get; }

	/// <summary>
	/// Gets the label the user gave the place.
	/// </summary>
	public string Label { get; }

	/// <summary>
	/// Gets where the place is; personal data, stored encrypted.
	/// </summary>
	public GeoPoint Point { get; }

	/// <summary>
	/// Gets the address in words; empty when unknown.
	/// </summary>
	public string AddressText { get; }
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SavedPlace"/> class.
	/// </summary>
	/// <param name="id">The identifier of the place.</param>
	/// <param name="kind">The kind of place.</param>
	/// <param name="label">The trimmed, non-empty label.</param>
	/// <param name="point">Where the place is.</param>
	/// <param name="addressText">The address in words.</param>
	private SavedPlace(SavedPlaceId id, PlaceKind kind, string label, GeoPoint point, string addressText)
		: base(id)
	{
		Kind = kind;
		Label = label;
		Point = point;
		AddressText = addressText;
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates a place after checking its label and point; only <see cref="SavedPlaces"/> creates places.
	/// </summary>
	/// <param name="kind">The kind of place.</param>
	/// <param name="label">The label; surrounding white space is dropped.</param>
	/// <param name="point">Where the place is.</param>
	/// <param name="addressText">The address in words; <see langword="null"/> means unknown.</param>
	/// <returns>The place, or a failure when the label is blank or the point is outside the Brussels box.</returns>
	internal static Result<SavedPlace> Create(PlaceKind kind, string? label, GeoPoint point, string? addressText)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return Result.Failure<SavedPlace>(SavedPlacesErrors.LabelRequired);
		}

		return IsInside(SavedPlaces.BrusselsBox, point)
			? new SavedPlace(SavedPlaceId.New(), kind, label.Trim(), point, addressText?.Trim() ?? string.Empty)
			: Result.Failure<SavedPlace>(SavedPlacesErrors.PointOutsideBrussels);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether a point lies inside a box, edges included.
	/// </summary>
	/// <param name="box">The box.</param>
	/// <param name="point">The point.</param>
	/// <returns><see langword="true"/> when the point is inside.</returns>
	private static bool IsInside(GeoBox box, GeoPoint point)
	{
		return point.Latitude >= box.South && point.Latitude <= box.North && point.Longitude >= box.West && point.Longitude <= box.East;
	}
	#endregion
}
