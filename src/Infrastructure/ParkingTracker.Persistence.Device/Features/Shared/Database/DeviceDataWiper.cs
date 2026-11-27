using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Removes the data of an account from the device database (ADR-0032). Each wipe stages every removal in one scope of the
///              unit of work and commits once, so that a failure leaves every table as it was. The repositories stage the removal of
///              their own tables; the removal of the sync state and of the owner binding is staged here, in the same scope.
/// </summary>
public sealed class DeviceDataWiper : IDeviceDataWiper
{
	#region Fields
	/// <summary>
	/// The unit of work that serialises the writes.
	/// </summary>
	private readonly IDeviceUnitOfWork unitOfWork;

	/// <summary>
	/// Brings the schema up to date before the wipe, so that every table exists.
	/// </summary>
	private readonly IDeviceDatabaseInitializer initializer;

	/// <summary>
	/// The repository of the saved places.
	/// </summary>
	private readonly ISavedPlacesRepository places;

	/// <summary>
	/// The repository of the vehicle profile.
	/// </summary>
	private readonly IVehicleRepository vehicles;

	/// <summary>
	/// The repository of the residents' card.
	/// </summary>
	private readonly IResidentCardRepository cards;

	/// <summary>
	/// The repository of the parking session.
	/// </summary>
	private readonly IParkingSessionRepository sessions;

	/// <summary>
	/// The repository of the sync state, which says whether a change is still waiting.
	/// </summary>
	private readonly ISyncStateRepository syncState;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceDataWiper"/> class.
	/// </summary>
	/// <param name="unitOfWork">The unit of work that serialises the writes.</param>
	/// <param name="initializer">Brings the schema up to date before the wipe.</param>
	/// <param name="places">The repository of the saved places.</param>
	/// <param name="vehicles">The repository of the vehicle profile.</param>
	/// <param name="cards">The repository of the residents' card.</param>
	/// <param name="sessions">The repository of the parking session.</param>
	/// <param name="syncState">The repository of the sync state.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public DeviceDataWiper(
		IDeviceUnitOfWork unitOfWork,
		IDeviceDatabaseInitializer initializer,
		ISavedPlacesRepository places,
		IVehicleRepository vehicles,
		IResidentCardRepository cards,
		IParkingSessionRepository sessions,
		ISyncStateRepository syncState)
	{
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(initializer);
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(vehicles);
		ArgumentNullException.ThrowIfNull(cards);
		ArgumentNullException.ThrowIfNull(sessions);
		ArgumentNullException.ThrowIfNull(syncState);
		this.unitOfWork = unitOfWork;
		this.initializer = initializer;
		this.places = places;
		this.vehicles = vehicles;
		this.cards = cards;
		this.sessions = sessions;
		this.syncState = syncState;
	}
	#endregion

	#region IDeviceDataWiper Implementation
	/// <inheritdoc />
	public Task<Result> WipeProfileAsync(CancellationToken cancellationToken = default) => WipeAsync(everything: false, onlyWhenSaved: false, cancellationToken);

	/// <inheritdoc />
	public async Task<Result<bool>> WipeProfileIfSavedAsync(CancellationToken cancellationToken = default)
	{
		var wiped = false;
		var result = await WipeAsync(everything: false, onlyWhenSaved: true, cancellationToken, () => wiped = true).ConfigureAwait(false);
		return result.IsFailure ? Result.Failure<bool>(result.Error, result.ResultExceptionType) : Result.Success(wiped);
	}

	/// <inheritdoc />
	public Task<Result> WipeAllAsync(CancellationToken cancellationToken = default) => WipeAsync(everything: true, onlyWhenSaved: false, cancellationToken);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Stages the removals in one scope and commits them.
	/// </summary>
	/// <param name="everything">Whether the parking session and the owner binding go too.</param>
	/// <param name="onlyWhenSaved">Whether nothing is removed while a change is waiting to be saved to the account; the flag is read inside the scope.</param>
	/// <param name="cancellationToken">Cancels the wipe before it starts.</param>
	/// <param name="removed">Called once the removal was committed.</param>
	/// <returns>A task with the result of the commit, or the failure of the schema.</returns>
	private async Task<Result> WipeAsync(bool everything, bool onlyWhenSaved, CancellationToken cancellationToken, Action? removed = null)
	{
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return ready;
		}

		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			if (onlyWhenSaved)
			{
				var current = await syncState.GetAsync(cancellationToken).ConfigureAwait(false);
				if (current.IsFailure)
				{
					return Result.Failure(current.Error, current.ResultExceptionType);
				}

				if (current.Value.IsDirty)
				{
					return Result.Success();
				}
			}

			places.Remove(scope);
			vehicles.Remove(scope);
			cards.Remove(scope);
			DeviceChangeScope.From(scope).Stage(connection => connection.Execute("DELETE FROM sync_state"));
			if (everything)
			{
				sessions.Remove(scope);
				DeviceChangeScope.From(scope).Stage(connection => connection.Execute("DELETE FROM device_owner"));
			}

			var committed = await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
			if (committed.IsSuccess)
			{
				removed?.Invoke();
			}

			return committed;
		}
	}
	#endregion
}
