using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="IVehicleStore"/> of the app (ADR-0031): the vehicle profile goes through the vehicle repository and the device
///              unit of work, with its plate encrypted. Like <see cref="DevicePlacesStore"/>, every read and write first waits for the start-up
///              gate and makes sure the schema exists, outside any scope. The current year that bounds the model year comes from the clock, never
///              from the domain. A profile that exists but cannot be read is never read as "none": the read throws
///              <see cref="SavedDataUnreadableException"/>; a write replaces a profile that is unreadable for good, but never one whose key
///              cannot be read now. When the key was lost (<see cref="DeviceDataErrors.KeyLost"/>), saving a vehicle removes the stale encrypted
///              rows in the same scope, and only then does a new key get created. Every save or removal stamps the synced vehicle section in
///              the same scope (ADR-0032), and a commit tells the sync trigger.
/// </summary>
public sealed partial class DeviceVehicleStore : IVehicleStore
{
	#region Fields
	/// <summary>
	/// The repository of the vehicle profile.
	/// </summary>
	private readonly IVehicleRepository vehicles;

	/// <summary>
	/// The repository of the saved places, whose encrypted rows go with the others when the key was lost.
	/// </summary>
	private readonly ISavedPlacesRepository places;

	/// <summary>
	/// The repository of the residents' card, whose encrypted rows go with the others when the key was lost.
	/// </summary>
	private readonly IResidentCardRepository cards;

	/// <summary>
	/// The repository of the parking session, whose encrypted rows go with the others when the key was lost.
	/// </summary>
	private readonly IParkingSessionRepository sessions;

	/// <summary>
	/// Begins the scope in which the repositories stage the changes of one write.
	/// </summary>
	private readonly IDeviceUnitOfWork unitOfWork;

	/// <summary>
	/// Makes sure the schema exists, also in a process that the start-up gate did not run in.
	/// </summary>
	private readonly IDeviceDatabaseInitializer initializer;

	/// <summary>
	/// The start-up gate that finishes before the saved values are read.
	/// </summary>
	private readonly IStartupGate gate;

	/// <summary>
	/// The clock that tells the current year.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<DeviceVehicleStore> logger;

	/// <summary>
	/// Tells the other stores that the stale rows were removed, so that they forget what they remembered.
	/// </summary>
	private readonly DeviceDataResetNotice resetNotice;

	/// <summary>
	/// Records the moment of a change of a synced section, in the scope of the change (ADR-0032).
	/// </summary>
	private readonly IProfileChangeTracker tracker;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceVehicleStore"/> class.
	/// </summary>
	/// <param name="vehicles">The repository of the vehicle profile.</param>
	/// <param name="places">The repository of the saved places.</param>
	/// <param name="cards">The repository of the residents' card.</param>
	/// <param name="sessions">The repository of the parking session.</param>
	/// <param name="unitOfWork">Begins the scope in which the repositories stage the changes of one write.</param>
	/// <param name="initializer">Makes sure the schema exists; it returns at once once the schema is current.</param>
	/// <param name="gate">The start-up gate that finishes before the saved values are read.</param>
	/// <param name="clock">The clock that tells the current year.</param>
	/// <param name="logger">The log.</param>
	/// <param name="resetNotice">Tells the other stores that the stale rows were removed.</param>
	/// <param name="tracker">Records the moment of a change of a synced section, in the scope of the change.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public DeviceVehicleStore(
		IVehicleRepository vehicles,
		ISavedPlacesRepository places,
		IResidentCardRepository cards,
		IParkingSessionRepository sessions,
		IDeviceUnitOfWork unitOfWork,
		IDeviceDatabaseInitializer initializer,
		IStartupGate gate,
		TimeProvider clock,
		ILogger<DeviceVehicleStore> logger,
		DeviceDataResetNotice resetNotice,
		IProfileChangeTracker tracker)
	{
		ArgumentNullException.ThrowIfNull(vehicles);
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(cards);
		ArgumentNullException.ThrowIfNull(sessions);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(initializer);
		ArgumentNullException.ThrowIfNull(gate);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(resetNotice);
		ArgumentNullException.ThrowIfNull(tracker);
		this.vehicles = vehicles;
		this.places = places;
		this.cards = cards;
		this.sessions = sessions;
		this.unitOfWork = unitOfWork;
		this.initializer = initializer;
		this.gate = gate;
		this.clock = clock;
		this.logger = logger;
		this.resetNotice = resetNotice;
		this.tracker = tracker;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a vehicle that could not be read; it is reported as unreadable, never as "none".
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The saved vehicle could not be read ({Code}); the user is asked to enter it again")]
	private static partial void LogUnreadable(ILogger logger, string code);

	/// <summary>
	/// Logs the schema of the database that could not be brought up to date.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The device database is not ready ({Code}); the read or write that follows reports its own failure")]
	private static partial void LogNotReady(ILogger logger, string code);

	/// <summary>
	/// Logs the removal of rows that a lost key left unreadable.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The key of the device database was lost; the stale encrypted rows are removed and the vehicle is saved with a new key")]
	private static partial void LogStaleRowsRemoved(ILogger logger);
	#endregion

	#region IVehicleStore Implementation
	/// <inheritdoc />
	public async Task<VehicleDetails?> GetAsync(CancellationToken cancellationToken = default)
	{
		await EnsureReadyAsync().ConfigureAwait(false);
		var loaded = await vehicles.GetAsync(cancellationToken).ConfigureAwait(false);
		if (loaded.IsSuccess)
		{
			return loaded.Value.ToDetails();
		}

		return DeviceDataErrors.IsNotStored(loaded) ? null : throw Unreadable(loaded);
	}

	/// <inheritdoc />
	public async Task<Result> SaveAsync(VehicleDetails details, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(details);
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var stored = await LoadForWriteAsync(cancellationToken).ConfigureAwait(false);
			var changed = Change(stored, details);
			if (changed.IsFailure)
			{
				return Result.Failure(changed.Error, changed.ResultExceptionType);
			}

			var staged = await vehicles.UpdateAsync(scope, changed.Value, cancellationToken).ConfigureAwait(false);
			if (DeviceDataErrors.IsKeyLost(staged))
			{
				staged = await ReplaceLostDataAsync(scope, changed.Value, cancellationToken).ConfigureAwait(false);
			}

			ThrowIfFailed(staged);
			tracker.Stage(scope, SyncSection.Vehicle);
			await CommitAsync(scope, cancellationToken).ConfigureAwait(false);
			tracker.NotifyCommitted();
			return Result.Success();
		}
	}

	/// <inheritdoc />
	public async Task RemoveAsync(CancellationToken cancellationToken = default)
	{
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			vehicles.Remove(scope);
			tracker.Stage(scope, SyncSection.Vehicle);
			await CommitAsync(scope, cancellationToken).ConfigureAwait(false);
			tracker.NotifyCommitted();
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Applies the entered values to the stored profile, which keeps its identifier, or creates the profile when none is stored.
	/// </summary>
	/// <param name="stored">The stored profile, or <see langword="null"/> when none is stored or it is unreadable for good.</param>
	/// <param name="details">The values as entered.</param>
	/// <returns>The profile to write, or the first rule the values break.</returns>
	private Result<Vehicle> Change(Vehicle? stored, VehicleDetails details)
	{
		var currentYear = clock.GetLocalNow().Year;
		if (stored is null)
		{
			return Vehicle.Create(details, currentYear);
		}

		var updated = stored.Update(details, currentYear);
		return updated.IsFailure ? Result.Failure<Vehicle>(updated.Error, updated.ResultExceptionType) : stored;
	}

	/// <summary>
	/// Reads the profile for a write: a profile that is unreadable for good is replaced by the write, so it counts as "none" here; a key
	/// that cannot be read now still throws, so that nothing is written meanwhile.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the profile, or <see langword="null"/> when none is stored or it is unreadable for good.</returns>
	/// <exception cref="SavedDataUnreadableException">The key cannot be read now.</exception>
	private async Task<Vehicle?> LoadForWriteAsync(CancellationToken cancellationToken)
	{
		var loaded = await vehicles.GetAsync(cancellationToken).ConfigureAwait(false);
		if (loaded.IsSuccess)
		{
			return loaded.Value;
		}

		if (DeviceDataErrors.IsNotStored(loaded))
		{
			return null;
		}

		var unreadable = Unreadable(loaded);
		return unreadable.IsTemporary ? throw unreadable : null;
	}

	/// <summary>
	/// Removes every encrypted row that the lost key left unreadable and stages the profile again, which then gets a new key. The removal
	/// is committed first, because the new key is created only when no encrypted row exists; both commits run in the one scope.
	/// </summary>
	/// <param name="scope">The scope of the write.</param>
	/// <param name="vehicle">The profile to store.</param>
	/// <param name="cancellationToken">Cancels the commit and the encryption.</param>
	/// <returns>A task with the result of staging the profile again.</returns>
	/// <exception cref="InvalidOperationException">The database refused the removal.</exception>
	private async Task<Result> ReplaceLostDataAsync(IDeviceChangeScope scope, Vehicle vehicle, CancellationToken cancellationToken)
	{
		LogStaleRowsRemoved(logger);
		await LostKeyCleanup.RemoveStaleRowsAsync(scope, cards, sessions, places, vehicles, resetNotice, cancellationToken).ConfigureAwait(false);
		return await vehicles.UpdateAsync(scope, vehicle, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Waits for the start-up gate and makes sure the schema exists. It must run before a scope begins and never inside one. A failed
	/// schema is logged and the call goes on: the read or write that follows reports its own failure.
	/// </summary>
	/// <returns>A task that completes when the gate has finished and the schema is checked.</returns>
	private async Task EnsureReadyAsync()
	{
		await gate.RunOnceAsync().ConfigureAwait(false);
		var ready = await initializer.InitializeAsync().ConfigureAwait(false);
		if (ready.IsFailure)
		{
			LogNotReady(logger, ready.Error.Code);
		}
	}

	/// <summary>
	/// Builds the exception for a vehicle that exists but cannot be read, and logs it. A key that cannot be read now is temporary.
	/// </summary>
	/// <param name="failed">The failed read.</param>
	/// <returns>The exception to throw.</returns>
	private SavedDataUnreadableException Unreadable(Result failed)
	{
		LogUnreadable(logger, failed.Error.Code);
		return new SavedDataUnreadableException(SavedDataUnreadableException.VehicleMessage, failed.Error.Code, DeviceDataErrors.IsKeyUnavailable(failed));
	}

	/// <summary>
	/// Commits the staged changes of a scope.
	/// </summary>
	/// <param name="scope">The scope of the write.</param>
	/// <param name="cancellationToken">Cancels the commit.</param>
	/// <returns>A task that completes when the changes are saved.</returns>
	/// <exception cref="InvalidOperationException">The database refused the changes.</exception>
	private static async Task CommitAsync(IDeviceChangeScope scope, CancellationToken cancellationToken)
	{
		ThrowIfFailed(await scope.CommitAsync(cancellationToken).ConfigureAwait(false));
	}

	/// <summary>
	/// Turns a failed result into the exception that the callers of a failed write already handle.
	/// </summary>
	/// <param name="result">The result to check.</param>
	/// <exception cref="InvalidOperationException">The result is a failure.</exception>
	private static void ThrowIfFailed(Result result)
	{
		if (result.IsFailure)
		{
			throw new InvalidOperationException(result.Error.Message);
		}
	}
	#endregion
}
