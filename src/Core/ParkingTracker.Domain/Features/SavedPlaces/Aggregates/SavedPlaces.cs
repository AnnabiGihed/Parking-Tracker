using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Primitives;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.SavedPlaces.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The places the user saved on this device: at most one home, at most one work place and at most 20 favourites, all
///              inside the Brussels box. There is one list per device, and it never leaves the phone.
/// </summary>
public sealed class SavedPlaces : LightweightAggregateRoot<SavedPlacesId>
{
	#region Constants
	/// <summary>
	/// The most favourite places a list holds.
	/// </summary>
	public const int MaximumFavourites = 20;
	#endregion

	#region Static Instances
	/// <summary>
	/// The box a saved place must lie in: the Brussels-Capital Region, i.e. the address search box (latitude 50.76 to
	/// 50.92, longitude 4.24 to 4.48) with a margin of about one kilometre, so places at the edge of the region still fit.
	/// </summary>
	public static readonly GeoBox BrusselsBox = new(50.75, 4.23, 50.93, 4.49);
	#endregion

	#region Fields
	/// <summary>
	/// The places in the order they were saved.
	/// </summary>
	private readonly List<SavedPlace> places = [];
	#endregion

	#region Properties
	/// <summary>
	/// Gets the saved places in the order they were saved.
	/// </summary>
	public IReadOnlyCollection<SavedPlace> Places => places.AsReadOnly();

	/// <summary>
	/// Gets the home, or <see langword="null"/> when none is saved.
	/// </summary>
	public SavedPlace? Home => places.FirstOrDefault(place => place.Kind == PlaceKind.Home);

	/// <summary>
	/// Gets the work place, or <see langword="null"/> when none is saved.
	/// </summary>
	public SavedPlace? Work => places.FirstOrDefault(place => place.Kind == PlaceKind.Work);
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SavedPlaces"/> class.
	/// </summary>
	/// <param name="id">The identifier of the list.</param>
	private SavedPlaces(SavedPlacesId id)
		: base(id)
	{
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the empty list of a device.
	/// </summary>
	/// <returns>A list without places.</returns>
	public static SavedPlaces Create() => new(SavedPlacesId.New());

	/// <summary>
	/// Rebuilds a list that was stored before, with its stored identifier, the identifiers of its places and the same rules as the
	/// behaviours: every place needs a label and a point inside the Brussels box, there is at most one home, one work place and 20
	/// favourites, and no identifier repeats. It raises no event: the list existed already.
	/// </summary>
	/// <param name="id">The stored identifier of the list.</param>
	/// <param name="stored">The stored places, in the order they were saved.</param>
	/// <returns>The list, or the first rule the stored places break.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static Result<SavedPlaces> Restore(SavedPlacesId id, IEnumerable<StoredPlace> stored)
	{
		ArgumentNullException.ThrowIfNull(id);
		ArgumentNullException.ThrowIfNull(stored);
		var list = new SavedPlaces(id);
		foreach (var item in stored)
		{
			var restored = SavedPlace.Restore(item);
			var accepted = restored.IsFailure ? Result.Failure(restored.Error, restored.ResultExceptionType) : list.Accept(restored.Value);
			if (accepted.IsFailure)
			{
				return Result.Failure<SavedPlaces>(accepted.Error, accepted.ResultExceptionType);
			}
		}

		return list;
	}
	#endregion

	#region Domain Behaviours
	/// <summary>
	/// Saves the home, replacing the one saved before.
	/// </summary>
	/// <param name="label">The label of the place.</param>
	/// <param name="point">Where the home is.</param>
	/// <param name="addressText">The address in words; <see langword="null"/> when unknown.</param>
	/// <returns>Success, or a failure when the label is blank or the point is outside the Brussels box; the old home then stays.</returns>
	public Result SetHome(string? label, GeoPoint point, string? addressText) => Replace(PlaceKind.Home, label, point, addressText);

	/// <summary>
	/// Saves the work place, replacing the one saved before.
	/// </summary>
	/// <param name="label">The label of the place.</param>
	/// <param name="point">Where the work place is.</param>
	/// <param name="addressText">The address in words; <see langword="null"/> when unknown.</param>
	/// <returns>Success, or a failure when the label is blank or the point is outside the Brussels box; the old work place then stays.</returns>
	public Result SetWork(string? label, GeoPoint point, string? addressText) => Replace(PlaceKind.Work, label, point, addressText);

	/// <summary>
	/// Adds a favourite place.
	/// </summary>
	/// <param name="label">The label of the place.</param>
	/// <param name="point">Where the place is.</param>
	/// <param name="addressText">The address in words; <see langword="null"/> when unknown.</param>
	/// <returns>The identifier of the new place, or a failure when the label is blank, the point is outside the Brussels box or there are 20 favourites already.</returns>
	public Result<SavedPlaceId> AddFavourite(string? label, GeoPoint point, string? addressText)
	{
		if (places.Count(place => place.Kind == PlaceKind.Favourite) >= MaximumFavourites)
		{
			return Result.Failure<SavedPlaceId>(SavedPlacesErrors.TooManyFavourites, ResultExceptionType.Conflict);
		}

		var created = SavedPlace.Create(PlaceKind.Favourite, label, point, addressText);
		if (created.IsFailure)
		{
			return Result.Failure<SavedPlaceId>(created.Error, created.ResultExceptionType);
		}

		places.Add(created.Value);
		return created.Value.Id;
	}

	/// <summary>
	/// Removes a saved place of any kind.
	/// </summary>
	/// <param name="id">The identifier of the place.</param>
	/// <returns>Success, or a not-found failure when no place has the identifier.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
	public Result Remove(SavedPlaceId id)
	{
		ArgumentNullException.ThrowIfNull(id);
		var place = places.FirstOrDefault(candidate => candidate.Id == id);
		if (place is null)
		{
			return Result.Failure(SavedPlacesErrors.PlaceNotFound, ResultExceptionType.NotFound);
		}

		places.Remove(place);
		return Result.Success();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Adds a restored place after checking that it does not repeat a home, a work place or an identifier, and that the favourites stay within the limit.
	/// </summary>
	/// <param name="place">The restored place.</param>
	/// <returns>Success, or the rule the place breaks.</returns>
	private Result Accept(SavedPlace place)
	{
		var repeats = places.Any(existing => existing.Id == place.Id || (place.Kind != PlaceKind.Favourite && existing.Kind == place.Kind));
		if (repeats)
		{
			return Result.Failure(SavedPlacesErrors.PlaceDuplicated, ResultExceptionType.Conflict);
		}

		if (place.Kind == PlaceKind.Favourite && places.Count(existing => existing.Kind == PlaceKind.Favourite) >= MaximumFavourites)
		{
			return Result.Failure(SavedPlacesErrors.TooManyFavourites, ResultExceptionType.Conflict);
		}

		places.Add(place);
		return Result.Success();
	}

	/// <summary>
	/// Saves the one place of a kind that may exist only once; the old one is dropped only when the new one is valid.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <param name="label">The label of the place.</param>
	/// <param name="point">Where the place is.</param>
	/// <param name="addressText">The address in words.</param>
	/// <returns>Success, or the failure of the place.</returns>
	private Result Replace(PlaceKind kind, string? label, GeoPoint point, string? addressText)
	{
		var created = SavedPlace.Create(kind, label, point, addressText);
		if (created.IsFailure)
		{
			return Result.Failure(created.Error, created.ResultExceptionType);
		}

		_ = places.RemoveAll(place => place.Kind == kind);
		places.Add(created.Value);
		return Result.Success();
	}
	#endregion
}
