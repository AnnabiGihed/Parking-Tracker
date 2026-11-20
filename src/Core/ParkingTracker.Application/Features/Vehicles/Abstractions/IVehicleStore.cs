using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Vehicles.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the vehicle profile of the My vehicle dialog and the side menu (ADR-0031). The profile lives in the device database
///              through the vehicle repository; the port hides the unit of work, the start-up gate, the encryption of the plate and the
///              clock that bounds the model year from the screens. A profile that exists but cannot be read is never read as "none": the
///              read throws <see cref="ParkingTracker.Application.Features.Shared.Abstractions.SavedDataUnreadableException"/>.
/// </summary>
public interface IVehicleStore
{
	#region Methods
	/// <summary>
	/// Reads the vehicle profile.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the saved values, or <see langword="null"/> when no vehicle is saved.</returns>
	/// <exception cref="ParkingTracker.Application.Features.Shared.Abstractions.SavedDataUnreadableException">A saved vehicle exists but cannot be read.</exception>
	Task<VehicleDetails?> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Saves the vehicle profile, replacing the one saved before.
	/// </summary>
	/// <param name="details">The values as entered; the domain checks them.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with success, or the first rule the values break (see <c>VehicleErrors</c>); nothing is written then.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="details"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The database refused the change.</exception>
	Task<Result> SaveAsync(VehicleDetails details, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes the vehicle profile; nothing happens when none is saved.
	/// </summary>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the profile is removed.</returns>
	/// <exception cref="InvalidOperationException">The database refused the change.</exception>
	Task RemoveAsync(CancellationToken cancellationToken = default);
	#endregion
}
