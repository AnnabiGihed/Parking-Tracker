using Pivot.Framework.Domain.Shared;
using SavedPlaceEntity = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlace;
using SavedPlacesList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Domain.Features.SavedPlaces.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the saved places; each code is stable and dotted.
/// </summary>
public static class SavedPlacesErrors
{
	#region Static Instances
	/// <summary>
	/// A place has no label.
	/// </summary>
	public static readonly Error LabelRequired = new("SavedPlaces.Label.Required", "A saved place needs a label.");

	/// <summary>
	/// A place has a label that is too long.
	/// </summary>
	public static readonly Error LabelTooLong = new("SavedPlaces.Label.TooLong", $"A saved place label has at most {SavedPlaceEntity.MaximumLabelLength} characters.");

	/// <summary>
	/// A place lies outside the Brussels box.
	/// </summary>
	public static readonly Error PointOutsideBrussels = new("SavedPlaces.Point.OutsideBrussels", "A saved place has to be inside the Brussels-Capital Region.");

	/// <summary>
	/// The list already holds the most favourites it can.
	/// </summary>
	public static readonly Error TooManyFavourites = new("SavedPlaces.Favourites.TooMany", $"You can save at most {SavedPlacesList.MaximumFavourites} favourite places.");

	/// <summary>
	/// No place of the list has the identifier.
	/// </summary>
	public static readonly Error PlaceNotFound = new("SavedPlaces.Place.NotFound", "The saved place was not found.");

	/// <summary>
	/// Stored places repeat a home, a work place or an identifier.
	/// </summary>
	public static readonly Error PlaceDuplicated = new("SavedPlaces.Place.Duplicated", "The stored places repeat a home, a work place or an identifier.");

	/// <summary>
	/// A stored place has a kind that does not exist.
	/// </summary>
	public static readonly Error KindUnknown = new("SavedPlaces.Kind.Unknown", "A stored place has an unknown kind.");
	#endregion
}
