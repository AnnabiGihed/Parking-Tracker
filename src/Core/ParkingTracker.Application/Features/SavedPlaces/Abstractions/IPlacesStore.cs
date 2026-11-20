using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SavedPlaces.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the Home, Work and favourite places of the shortcut chips and the menu. The places live in the device database through the saved
///              places list; the port hides the unit of work, the start-up gate and the encryption from the screens. A place that exists
///              but cannot be read is never read as "none": the read throws <see cref="ParkingTracker.Application.Features.Shared.Abstractions.SavedDataUnreadableException"/>.
/// </summary>
public interface IPlacesStore
{
	#region Methods
	/// <summary>
	/// Reads the places that are saved: Home, Work and the favourites.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the saved places in the order home, work, then the favourites as they were saved; empty when none is saved.</returns>
	/// <exception cref="ParkingTracker.Application.Features.Shared.Abstractions.SavedDataUnreadableException">A saved place exists but cannot be read.</exception>
	Task<IReadOnlyList<PlaceShortcut>> GetPlacesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Saves Home or Work, replacing the one saved before. The label is the name of the kind.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <param name="point">Where the place is.</param>
	/// <param name="addressText">The address in words; <see langword="null"/> when unknown.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with success, or the failure of the place (for example a point outside Brussels); nothing is written then.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not home or work.</exception>
	/// <exception cref="InvalidOperationException">The database refused the change.</exception>
	Task<Result> SavePlaceAsync(PlaceKind kind, GeoPoint point, string? addressText, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes Home or Work; nothing happens when it is not saved.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the place is removed.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not home or work.</exception>
	/// <exception cref="InvalidOperationException">The database refused the change.</exception>
	Task ClearPlaceAsync(PlaceKind kind, CancellationToken cancellationToken = default);

	/// <summary>
	/// Adds a favourite place.
	/// </summary>
	/// <param name="label">The name the user gave the place.</param>
	/// <param name="point">Where the place is.</param>
	/// <param name="addressText">The address in words; <see langword="null"/> when unknown.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with success, or the failure of the favourite (a blank or too long label, a point outside Brussels, 20 favourites already); nothing is written then.</returns>
	/// <exception cref="InvalidOperationException">The database refused the change.</exception>
	Task<Result> AddFavouriteAsync(string label, GeoPoint point, string? addressText, CancellationToken cancellationToken = default);

	/// <summary>
	/// Changes the name and the place of a favourite; it keeps its identifier and its position.
	/// </summary>
	/// <param name="id">The identifier of the favourite.</param>
	/// <param name="label">The new name.</param>
	/// <param name="point">The new place.</param>
	/// <param name="addressText">The new address in words; <see langword="null"/> when unknown.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with success, a not-found failure when the favourite is gone, or the failure of the new values; nothing is written then.</returns>
	/// <exception cref="InvalidOperationException">The database refused the change.</exception>
	Task<Result> UpdateFavouriteAsync(SavedPlaceId id, string label, GeoPoint point, string? addressText, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a favourite; nothing happens when it is not saved.
	/// </summary>
	/// <param name="id">The identifier of the favourite.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the favourite is removed.</returns>
	/// <exception cref="InvalidOperationException">The database refused the change.</exception>
	Task RemoveFavouriteAsync(SavedPlaceId id, CancellationToken cancellationToken = default);
	#endregion
}
