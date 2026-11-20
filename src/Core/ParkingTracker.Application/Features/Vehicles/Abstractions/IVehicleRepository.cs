using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Vehicles.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the vehicle profile on the device (ADR-0031). The phone holds one profile, so the port loads "the" profile
///              and a change replaces whatever is stored; the profile keeps its identifier across loads. Changes are staged in the
///              <see cref="ParkingTracker.Application.Features.Shared.Abstractions.IDeviceChangeScope"/> of the operation and applied
///              when the scope commits.
/// </summary>
public interface IVehicleRepository
{
	#region Methods
	/// <summary>
	/// Loads the profile, rebuilt through <see cref="Vehicle.Restore"/> so that a stored profile that breaks a rule is refused.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the profile; the failure <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.NotStored"/>
	/// when none is stored; <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/> when the
	/// key cannot be read now (the profile may exist); or another failure when a field cannot be decrypted, the stored profile breaks a rule
	/// or the database fails. A profile is never returned with fields left out.</returns>
	Task<Result<Vehicle>> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages a new profile for the next commit; nothing happens at commit when a profile is stored already, so that a newer profile is never overwritten.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="vehicle">The profile to store.</param>
	/// <param name="cancellationToken">Cancels the encryption of its fields.</param>
	/// <returns>A task with success, or a failure when the fields cannot be encrypted.</returns>
	Task<Result> AddAsync(IDeviceChangeScope scope, Vehicle vehicle, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the replacement of the stored profile by <paramref name="vehicle"/> for the next commit.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="vehicle">The profile that replaces the stored one.</param>
	/// <param name="cancellationToken">Cancels the encryption of its fields.</param>
	/// <returns>A task with success, or a failure when the fields cannot be encrypted.</returns>
	Task<Result> UpdateAsync(IDeviceChangeScope scope, Vehicle vehicle, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the removal of the stored profile for the next commit; nothing happens at commit when none is stored.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	void Remove(IDeviceChangeScope scope);
	#endregion
}
