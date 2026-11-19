using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SavedPlaces.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the Home and Work places of the shortcut chips. The places live in the device database through the saved
///              places list; the port hides the unit of work, the start-up gate and the encryption from the screens. A place that exists
///              but cannot be read is never read as "none": the read throws <see cref="ParkingTracker.Application.Features.Shared.Abstractions.SavedDataUnreadableException"/>.
/// </summary>
public interface IPlacesStore
{
	#region Methods
	/// <summary>
	/// Reads the Home and Work places that are saved.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the saved places in the order home, work; empty when none is saved.</returns>
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
	#endregion
}
