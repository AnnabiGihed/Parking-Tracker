using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Shared;
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;
using SavedPlace = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlace;

namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="IPlacesStore"/> of the app (ADR-0026): the Home and Work places go through the repository of the saved
///              places list and the device unit of work, with their coordinates encrypted per field. Like <see cref="DeviceSettingsStore"/>,
///              every read and write first waits for the start-up gate and makes sure the schema exists, outside any scope. A place that
///              exists but cannot be read is never read as "none": the read throws <see cref="SavedDataUnreadableException"/>; a write
///              replaces a list that is unreadable for good, but never one whose key cannot be read now. When the key was lost
///              (<see cref="DeviceDataErrors.KeyLost"/>), saving a place removes the stale encrypted rows in the same scope, and only then
///              does a new key get created.
/// </summary>
public sealed partial class DevicePlacesStore : IPlacesStore
{
	#region Fields
	/// <summary>
	/// The repository of the saved places list.
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
	/// The log.
	/// </summary>
	private readonly ILogger<DevicePlacesStore> logger;

	/// <summary>
	/// Tells the other stores that the stale rows were removed, so that they forget what they remembered.
	/// </summary>
	private readonly DeviceDataResetNotice resetNotice;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DevicePlacesStore"/> class.
	/// </summary>
	/// <param name="places">The repository of the saved places list.</param>
	/// <param name="cards">The repository of the residents' card.</param>
	/// <param name="sessions">The repository of the parking session.</param>
	/// <param name="unitOfWork">Begins the scope in which the repositories stage the changes of one write.</param>
	/// <param name="initializer">Makes sure the schema exists; it returns at once once the schema is current.</param>
	/// <param name="gate">The start-up gate that finishes before the saved values are read.</param>
	/// <param name="logger">The log.</param>
	/// <param name="resetNotice">Tells the other stores that the stale rows were removed.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public DevicePlacesStore(
		ISavedPlacesRepository places,
		IResidentCardRepository cards,
		IParkingSessionRepository sessions,
		IDeviceUnitOfWork unitOfWork,
		IDeviceDatabaseInitializer initializer,
		IStartupGate gate,
		ILogger<DevicePlacesStore> logger,
		DeviceDataResetNotice resetNotice)
	{
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(cards);
		ArgumentNullException.ThrowIfNull(sessions);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(initializer);
		ArgumentNullException.ThrowIfNull(gate);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(resetNotice);
		this.places = places;
		this.cards = cards;
		this.sessions = sessions;
		this.unitOfWork = unitOfWork;
		this.initializer = initializer;
		this.gate = gate;
		this.logger = logger;
		this.resetNotice = resetNotice;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs saved places that could not be read; they are reported as unreadable, never as "none".
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The saved places could not be read ({Code}); the user is asked to set them again")]
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
	[LoggerMessage(Level = LogLevel.Warning, Message = "The key of the device database was lost; the stale encrypted rows are removed and the place is saved with a new key")]
	private static partial void LogStaleRowsRemoved(ILogger logger);
	#endregion

	#region IPlacesStore Implementation
	/// <inheritdoc />
	public async Task<IReadOnlyList<PlaceShortcut>> GetPlacesAsync(CancellationToken cancellationToken = default)
	{
		await EnsureReadyAsync().ConfigureAwait(false);
		var loaded = await places.GetAsync(cancellationToken).ConfigureAwait(false);
		if (loaded.IsFailure)
		{
			return DeviceDataErrors.IsNotStored(loaded) ? [] : throw Unreadable(loaded);
		}

		return ToShortcuts(loaded.Value);
	}

	/// <inheritdoc />
	public async Task<Result> SavePlaceAsync(PlaceKind kind, GeoPoint point, string? addressText, CancellationToken cancellationToken = default)
	{
		ThrowIfNotShortcut(kind);
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var list = await LoadForWriteAsync(cancellationToken).ConfigureAwait(false) ?? PlaceList.Create();
			var set = kind == PlaceKind.Home ? list.SetHome(kind.ToString(), point, addressText) : list.SetWork(kind.ToString(), point, addressText);
			if (set.IsFailure)
			{
				return set;
			}

			var staged = await places.UpdateAsync(scope, list, cancellationToken).ConfigureAwait(false);
			if (DeviceDataErrors.IsKeyLost(staged))
			{
				staged = await ReplaceLostDataAsync(scope, list, cancellationToken).ConfigureAwait(false);
			}

			ThrowIfFailed(staged);
			await CommitAsync(scope, cancellationToken).ConfigureAwait(false);
			return Result.Success();
		}
	}

	/// <inheritdoc />
	public async Task ClearPlaceAsync(PlaceKind kind, CancellationToken cancellationToken = default)
	{
		ThrowIfNotShortcut(kind);
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var list = await LoadForWriteAsync(cancellationToken).ConfigureAwait(false);
			var place = list is null ? null : FindShortcut(list, kind);
			if (list is null || place is null)
			{
				return;
			}

			ThrowIfFailed(list.Remove(place.Id));
			if (list.Places.Count == 0)
			{
				places.Remove(scope);
			}
			else
			{
				ThrowIfFailed(await places.UpdateAsync(scope, list, cancellationToken).ConfigureAwait(false));
			}

			await CommitAsync(scope, cancellationToken).ConfigureAwait(false);
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the list for a write: a list that is unreadable for good is replaced by the write, so it counts as "none" here; a key
	/// that cannot be read now still throws, so that nothing is written meanwhile.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the list, or <see langword="null"/> when none is stored or it is unreadable for good.</returns>
	/// <exception cref="SavedDataUnreadableException">The key cannot be read now.</exception>
	private async Task<PlaceList?> LoadForWriteAsync(CancellationToken cancellationToken)
	{
		var loaded = await places.GetAsync(cancellationToken).ConfigureAwait(false);
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
	/// Removes every encrypted row that the lost key left unreadable and stages the list again, which then gets a new key. The removal
	/// is committed first, because the new key is created only when no encrypted row exists; both commits run in the one scope.
	/// </summary>
	/// <param name="scope">The scope of the write.</param>
	/// <param name="list">The list to store.</param>
	/// <param name="cancellationToken">Cancels the commit and the encryption.</param>
	/// <returns>A task with the result of staging the list again.</returns>
	/// <exception cref="InvalidOperationException">The database refused the removal.</exception>
	private async Task<Result> ReplaceLostDataAsync(IDeviceChangeScope scope, PlaceList list, CancellationToken cancellationToken)
	{
		LogStaleRowsRemoved(logger);
		cards.Remove(scope);
		sessions.Remove(scope);
		places.Remove(scope);
		await CommitAsync(scope, cancellationToken).ConfigureAwait(false);
		resetNotice.Raise();
		return await places.UpdateAsync(scope, list, cancellationToken).ConfigureAwait(false);
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
	/// Builds the exception for saved places that exist but cannot be read, and logs it. A key that cannot be read now is temporary.
	/// </summary>
	/// <param name="failed">The failed read.</param>
	/// <returns>The exception to throw.</returns>
	private SavedDataUnreadableException Unreadable(Result failed)
	{
		LogUnreadable(logger, failed.Error.Code);
		return new SavedDataUnreadableException(SavedDataUnreadableException.PlacesMessage, failed.Error.Code, DeviceDataErrors.IsKeyUnavailable(failed));
	}

	/// <summary>
	/// Maps the home and the work place of a list to shortcuts, home first.
	/// </summary>
	/// <param name="list">The list.</param>
	/// <returns>The shortcuts; empty when neither is saved.</returns>
	private static List<PlaceShortcut> ToShortcuts(PlaceList list)
	{
		return new[] { list.Home, list.Work }
			.OfType<SavedPlace>()
			.Select(place => new PlaceShortcut(place.Kind, place.Label, place.Point, place.AddressText))
			.ToList();
	}

	/// <summary>
	/// Finds the home or the work place of a list.
	/// </summary>
	/// <param name="list">The list.</param>
	/// <param name="kind">Home or work.</param>
	/// <returns>The place, or <see langword="null"/> when it is not saved.</returns>
	private static SavedPlace? FindShortcut(PlaceList list, PlaceKind kind) => kind == PlaceKind.Home ? list.Home : list.Work;

	/// <summary>
	/// Refuses a kind that is not a shortcut.
	/// </summary>
	/// <param name="kind">The kind to check.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not home or work.</exception>
	private static void ThrowIfNotShortcut(PlaceKind kind)
	{
		if (kind is not (PlaceKind.Home or PlaceKind.Work))
		{
			throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only the home and the work place have a shortcut.");
		}
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
