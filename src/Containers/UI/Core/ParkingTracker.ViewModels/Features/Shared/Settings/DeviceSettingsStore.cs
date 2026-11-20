using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.ParkingSessions.Aggregates;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using ParkingTracker.Domain.Features.ResidentCards.Errors;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ISettingsStore"/> of the app once the device database exists (ADR-0026): the card, the parked car
///              and the timer go through the repositories of the <see cref="ResidentCard"/> and <see cref="ParkingSession"/>
///              aggregates and the device unit of work; every other value (alerts, layers, welcome, permission snooze) stays in the
///              preferences through the inner store. The screens, handlers and test hooks keep calling the same port. The card is
///              kept in memory after the first successful read, because every spot check asks for it and decrypting is slow. A
///              timer that the session cannot hold (no car is saved, or its length is outside 5 minutes to one day, as the test hook
///              starts one) stays in the preferences, where the first version kept it, and is read from there. Every write begins its
///              own scope of the unit of work, which also keeps the move of the old settings out of its way; a save replaces what is
///              stored.
///              A card or car that exists but cannot be read (a lost key, a changed value, a row that breaks a rule, a key that cannot
///              be read now) is never read as "nothing saved": the read throws <see cref="SavedDataUnreadableException"/>, the spot check
///              answers "can't verify" and asks for the card again, and the state is remembered, so it is neither read nor logged again on
///              every check, until the user saves or clears the value (a key that cannot be read now is retried every time, and nothing is
///              written meanwhile). When the key was lost (<see cref="DeviceDataErrors.KeyLost"/>), saving the card removes the stale
///              encrypted rows in the same scope, and only then does a new key get created.
///              Every read and every write first waits for the start-up gate, outside any scope, because a background reader may run
///              before the screens and must not cache "no card" while the old values are still being moved. A change made before the
///              one-time move has finished also removes the old copy of the value, so that the next launch cannot bring deleted data back.
/// </summary>
public sealed partial class DeviceSettingsStore : ISettingsStore, IDisposable
{
	#region Fields
	/// <summary>
	/// The store of the values that stay in the preferences, and of the timer that the session cannot hold.
	/// </summary>
	private readonly ISettingsStore inner;

	/// <summary>
	/// The repository of the residents' card.
	/// </summary>
	private readonly IResidentCardRepository cards;

	/// <summary>
	/// The repository of the parking session.
	/// </summary>
	private readonly IParkingSessionRepository sessions;

	/// <summary>
	/// The repository of the saved places, whose encrypted rows go with the others when the key was lost.
	/// </summary>
	private readonly ISavedPlacesRepository places;

	/// <summary>
	/// The repository of the vehicle profile, whose encrypted plate goes with the other rows when the key was lost.
	/// </summary>
	private readonly IVehicleRepository vehicles;

	/// <summary>
	/// Begins the scope in which the repositories stage the changes of one write.
	/// </summary>
	private readonly IDeviceUnitOfWork unitOfWork;

	/// <summary>
	/// Makes sure the schema exists, also in a process that the start-up gate did not run in (a background receiver).
	/// </summary>
	private readonly IDeviceDatabaseInitializer initializer;

	/// <summary>
	/// The start-up gate that finishes before the saved values are read.
	/// </summary>
	private readonly IStartupGate gate;

	/// <summary>
	/// The municipalities that a saved card is checked against.
	/// </summary>
	private readonly IReferenceData referenceData;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<DeviceSettingsStore> logger;

	/// <summary>
	/// Tells this store that another store removed the stale encrypted rows, so that it forgets the card and the unreadable states.
	/// </summary>
	private readonly DeviceDataResetNotice resetNotice;

	/// <summary>
	/// Guards <see cref="cachedCard"/>, <see cref="isCardCached"/>, <see cref="cardUnreadableCode"/> and <see cref="sessionUnreadableCode"/>.
	/// </summary>
	private readonly Lock cacheGate = new();

	/// <summary>
	/// The card that was read or saved last; meaningful only while <see cref="isCardCached"/> is set.
	/// </summary>
	private ParkingCard? cachedCard;

	/// <summary>
	/// Whether <see cref="cachedCard"/> holds the answer, including "no card".
	/// </summary>
	private bool isCardCached;

	/// <summary>
	/// The code of the error that made the saved card unreadable, or <see langword="null"/> while it is readable or not read yet.
	/// </summary>
	private string? cardUnreadableCode;

	/// <summary>
	/// The code of the error that made the saved parked car unreadable, or <see langword="null"/> while it is readable or not read yet.
	/// </summary>
	private string? sessionUnreadableCode;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceSettingsStore"/> class.
	/// </summary>
	/// <param name="inner">The store of the values that stay in the preferences.</param>
	/// <param name="cards">The repository of the residents' card.</param>
	/// <param name="sessions">The repository of the parking session.</param>
	/// <param name="places">The repository of the saved places.</param>
	/// <param name="vehicles">The repository of the vehicle profile.</param>
	/// <param name="unitOfWork">Begins the scope in which the repositories stage the changes of one write.</param>
	/// <param name="initializer">Makes sure the schema exists; it returns at once once the schema is current.</param>
	/// <param name="gate">The start-up gate that finishes before the saved values are read.</param>
	/// <param name="referenceData">The municipalities that a saved card is checked against.</param>
	/// <param name="logger">The log.</param>
	/// <param name="resetNotice">Tells this store that the stale encrypted rows were removed; the store listens until it is disposed.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public DeviceSettingsStore(
		ISettingsStore inner,
		IResidentCardRepository cards,
		IParkingSessionRepository sessions,
		ISavedPlacesRepository places,
		IVehicleRepository vehicles,
		IDeviceUnitOfWork unitOfWork,
		IDeviceDatabaseInitializer initializer,
		IStartupGate gate,
		IReferenceData referenceData,
		ILogger<DeviceSettingsStore> logger,
		DeviceDataResetNotice resetNotice)
	{
		ArgumentNullException.ThrowIfNull(inner);
		ArgumentNullException.ThrowIfNull(cards);
		ArgumentNullException.ThrowIfNull(sessions);
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(vehicles);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(initializer);
		ArgumentNullException.ThrowIfNull(gate);
		ArgumentNullException.ThrowIfNull(referenceData);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(resetNotice);
		this.inner = inner;
		this.cards = cards;
		this.sessions = sessions;
		this.places = places;
		this.vehicles = vehicles;
		this.unitOfWork = unitOfWork;
		this.initializer = initializer;
		this.gate = gate;
		this.referenceData = referenceData;
		this.logger = logger;
		this.resetNotice = resetNotice;
		resetNotice.StaleRowsRemoved += OnStaleRowsRemoved;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a stored value that could not be read; it is reported as unreadable, never as "nothing saved".
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="what">What was read: the card or the parking session.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The saved {What} could not be read ({Code}); the user is asked to enter it again")]
	private static partial void LogUnreadable(ILogger logger, string what, string code);

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
	[LoggerMessage(Level = LogLevel.Warning, Message = "The key of the device database was lost; the stale encrypted rows are removed and the card is saved with a new key")]
	private static partial void LogStaleRowsRemoved(ILogger logger);

	/// <summary>
	/// Logs the old copy of a value that could not be removed after the user changed the value.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the storage reported.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The old copy of a changed value could not be removed; it may be moved into the database at the next launch")]
	private static partial void LogOldCopyKept(ILogger logger, Exception exception);
	#endregion

	#region ISettingsStore Implementation
	/// <inheritdoc />
	public async Task<ParkingCard?> GetCardAsync()
	{
		lock (cacheGate)
		{
			if (isCardCached)
			{
				return cachedCard;
			}

			if (cardUnreadableCode is { } code)
			{
				throw new SavedDataUnreadableException(SavedDataUnreadableException.CardMessage, code, isTemporary: false);
			}
		}

		await EnsureReadyAsync().ConfigureAwait(false);
		var loaded = await cards.GetAsync().ConfigureAwait(false);
		if (loaded.IsFailure && !DeviceDataErrors.IsNotStored(loaded))
		{
			throw Unreadable("card", SavedDataUnreadableException.CardMessage, loaded, remember: code => cardUnreadableCode = code);
		}

		var card = loaded.IsSuccess ? loaded.Value.ToParkingCard() : null;
		CacheCard(card);
		return card;
	}

	/// <inheritdoc />
	public async Task SaveCardAsync(ParkingCard card)
	{
		ArgumentNullException.ThrowIfNull(card);
		await EnsureReadyAsync().ConfigureAwait(false);
		var aggregate = CreateCard(card);
		var scope = await unitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var staged = await cards.UpdateAsync(scope, aggregate).ConfigureAwait(false);
			if (DeviceDataErrors.IsKeyLost(staged))
			{
				staged = await ReplaceLostDataAsync(scope, aggregate).ConfigureAwait(false);
			}

			ThrowIfFailed(staged);
			await CommitAsync(scope).ConfigureAwait(false);
		}

		// What the next start would read: the aggregate has trimmed the number and normalised the plate.
		CacheCard(aggregate.ToParkingCard());
		await RemoveOldCopyAsync(inner.ClearCardAsync).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task ClearCardAsync()
	{
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			cards.Remove(scope);
			await CommitAsync(scope).ConfigureAwait(false);
		}

		CacheCard(null);
		await RemoveOldCopyAsync(inner.ClearCardAsync).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<ParkedCar?> GetCarAsync() => (await LoadSessionAsync().ConfigureAwait(false))?.ToParkedCar();

	/// <inheritdoc />
	public async Task SaveCarAsync(ParkedCar car)
	{
		ArgumentNullException.ThrowIfNull(car);
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var timer = (await LoadSessionForWriteAsync().ConfigureAwait(false))?.Timer ?? await inner.GetTimerAsync().ConfigureAwait(false);
			var session = ParkingSession.FromParkedCar(car, timer);
			var keepsTimer = session.IsSuccess;
			if (session.IsFailure && timer is not null)
			{
				// The timer does not fit the new car (it ended before it was parked): the timer stays where the old version kept it.
				session = ParkingSession.FromParkedCar(car, null);
			}

			ThrowIfFailed(session);
			ThrowIfFailed(await sessions.UpdateAsync(scope, session.Value).ConfigureAwait(false));
			await CommitAsync(scope).ConfigureAwait(false);
			ForgetUnreadableSession();
			await MoveTimerAsync(timer, keepsTimer).ConfigureAwait(false);
		}

		await RemoveOldCopyAsync(inner.ClearCarAsync).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task ClearCarAsync()
	{
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var session = await LoadSessionForWriteAsync().ConfigureAwait(false);
			sessions.Remove(scope);
			await CommitAsync(scope).ConfigureAwait(false);
			ForgetUnreadableSession();

			// Forgetting the car does not stop the timer; the view model does that as a separate step, as it always did.
			if (session?.Timer is { } timer)
			{
				await inner.SaveTimerAsync(timer).ConfigureAwait(false);
			}
		}

		await RemoveOldCopyAsync(inner.ClearCarAsync).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<ParkingTimer?> GetTimerAsync()
	{
		return (await LoadSessionAsync().ConfigureAwait(false))?.Timer ?? await inner.GetTimerAsync().ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task SaveTimerAsync(ParkingTimer timer)
	{
		ArgumentNullException.ThrowIfNull(timer);
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var session = await LoadSessionForWriteAsync().ConfigureAwait(false);
			if (session is not null && session.StartTimer(timer).IsSuccess)
			{
				await StoreSessionAsync(scope, session).ConfigureAwait(false);
				await inner.ClearTimerAsync().ConfigureAwait(false);
				return;
			}

			await DropSessionTimerAsync(scope, session).ConfigureAwait(false);
			await inner.SaveTimerAsync(timer).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task ClearTimerAsync()
	{
		await EnsureReadyAsync().ConfigureAwait(false);
		var scope = await unitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			await DropSessionTimerAsync(scope, await LoadSessionForWriteAsync().ConfigureAwait(false)).ConfigureAwait(false);
			await inner.ClearTimerAsync().ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public Task<bool> GetAlertsEnabledAsync() => inner.GetAlertsEnabledAsync();

	/// <inheritdoc />
	public Task SaveAlertsEnabledAsync(bool enabled) => inner.SaveAlertsEnabledAsync(enabled);

	/// <inheritdoc />
	public Task<LayerSettings> GetLayersAsync() => inner.GetLayersAsync();

	/// <inheritdoc />
	public Task SaveLayersAsync(LayerSettings layers) => inner.SaveLayersAsync(layers);

	/// <inheritdoc />
	public Task<bool> GetWelcomeSeenAsync() => inner.GetWelcomeSeenAsync();

	/// <inheritdoc />
	public Task SaveWelcomeSeenAsync() => inner.SaveWelcomeSeenAsync();

	/// <inheritdoc />
	public Task<DateTimeOffset?> GetPermissionSnoozeUntilAsync() => inner.GetPermissionSnoozeUntilAsync();

	/// <inheritdoc />
	public Task SavePermissionSnoozeUntilAsync(DateTimeOffset until) => inner.SavePermissionSnoozeUntilAsync(until);
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		resetNotice.StaleRowsRemoved -= OnStaleRowsRemoved;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Forgets the card that was read and every remembered unreadable state, after the stale encrypted rows were removed: the next read
	/// goes to the repository.
	/// </summary>
	/// <param name="sender">The notice.</param>
	/// <param name="e">Not used.</param>
	private void OnStaleRowsRemoved(object? sender, EventArgs e)
	{
		lock (cacheGate)
		{
			cachedCard = null;
			isCardCached = false;
			cardUnreadableCode = null;
			sessionUnreadableCode = null;
		}
	}

	/// <summary>
	/// Reads the open parking session. A session that exists but cannot be read is not "none": the call throws
	/// <see cref="SavedDataUnreadableException"/>, and a permanent failure is remembered until the car is saved or cleared.
	/// </summary>
	/// <returns>A task with the session, or <see langword="null"/>.</returns>
	/// <exception cref="SavedDataUnreadableException">The saved session cannot be read.</exception>
	private async Task<ParkingSession?> LoadSessionAsync()
	{
		lock (cacheGate)
		{
			if (sessionUnreadableCode is { } code)
			{
				throw new SavedDataUnreadableException(SavedDataUnreadableException.CarMessage, code, isTemporary: false);
			}
		}

		await EnsureReadyAsync().ConfigureAwait(false);
		var loaded = await sessions.GetAsync().ConfigureAwait(false);
		if (loaded.IsFailure)
		{
			return DeviceDataErrors.IsNotStored(loaded)
				? null
				: throw Unreadable("parking session", SavedDataUnreadableException.CarMessage, loaded, remember: code => sessionUnreadableCode = code);
		}

		return loaded.Value.IsOpen ? loaded.Value : null;
	}

	/// <summary>
	/// Reads the open session for a write: a session that is unreadable for good is replaced or removed by the write, so it counts as
	/// "none" here; a key that cannot be read now still throws, so that nothing is written meanwhile.
	/// </summary>
	/// <returns>A task with the session, or <see langword="null"/>.</returns>
	/// <exception cref="SavedDataUnreadableException">The key cannot be read now.</exception>
	private async Task<ParkingSession?> LoadSessionForWriteAsync()
	{
		try
		{
			return await LoadSessionAsync().ConfigureAwait(false);
		}
		catch (SavedDataUnreadableException unreadable) when (!unreadable.IsTemporary)
		{
			return null;
		}
	}

	/// <summary>
	/// Removes every encrypted row that the lost key left unreadable and stages the card again, which then gets a new key. The removal
	/// is committed first, because the new key is created only when no encrypted row exists; both commits run in the one scope.
	/// </summary>
	/// <param name="scope">The scope of the write.</param>
	/// <param name="aggregate">The card the user entered.</param>
	/// <returns>A task with the result of staging the card again.</returns>
	/// <exception cref="InvalidOperationException">The database refused the removal.</exception>
	private async Task<Result> ReplaceLostDataAsync(IDeviceChangeScope scope, ResidentCard aggregate)
	{
		LogStaleRowsRemoved(logger);

		// Raising the notice also makes this store forget the cached card and every remembered unreadable state.
		await LostKeyCleanup.RemoveStaleRowsAsync(scope, cards, sessions, places, vehicles, resetNotice).ConfigureAwait(false);
		return await cards.UpdateAsync(scope, aggregate).ConfigureAwait(false);
	}

	/// <summary>
	/// Writes a changed session over the stored one.
	/// </summary>
	/// <param name="scope">The scope of the write.</param>
	/// <param name="session">The session.</param>
	/// <returns>A task that completes when the session is committed.</returns>
	private async Task StoreSessionAsync(IDeviceChangeScope scope, ParkingSession session)
	{
		ThrowIfFailed(await sessions.UpdateAsync(scope, session).ConfigureAwait(false));
		await CommitAsync(scope).ConfigureAwait(false);
	}

	/// <summary>
	/// Removes the timer from the stored session, so that a timer in the preferences is the one that is read.
	/// </summary>
	/// <param name="scope">The scope of the write.</param>
	/// <param name="session">The session, or <see langword="null"/> when none is saved.</param>
	/// <returns>A task that completes when the session is committed.</returns>
	private async Task DropSessionTimerAsync(IDeviceChangeScope scope, ParkingSession? session)
	{
		if (session?.Timer is not null && session.ClearTimer().IsSuccess)
		{
			await StoreSessionAsync(scope, session).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// After a new car replaced the session, puts the timer where it is read: in the new session, or in the preferences.
	/// </summary>
	/// <param name="timer">The timer that ran before, or <see langword="null"/>.</param>
	/// <param name="keptInSession">Whether the new session took the timer over.</param>
	/// <returns>A task that completes when the timer is in place.</returns>
	private async Task MoveTimerAsync(ParkingTimer? timer, bool keptInSession)
	{
		if (timer is null)
		{
			return;
		}

		await (keptInSession ? inner.ClearTimerAsync() : inner.SaveTimerAsync(timer)).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds the card aggregate against the municipality the card names.
	/// </summary>
	/// <param name="card">The card to save.</param>
	/// <returns>The checked card.</returns>
	/// <exception cref="InvalidOperationException">The reference data is missing, or the card breaks a rule of its municipality.</exception>
	private ResidentCard CreateCard(ParkingCard card)
	{
		var data = referenceData.GetReferenceData();
		ThrowIfFailed(data);
		var municipality = data.Value.Municipalities.FirstOrDefault(candidate => string.Equals(candidate.Id, card.Municipality, StringComparison.Ordinal))
			?? throw new InvalidOperationException(ResidentCardErrors.MunicipalityMismatch.Message);
		var created = ResidentCard.Create(municipality, card);
		ThrowIfFailed(created);
		return created.Value;
	}

	/// <summary>
	/// Waits for the start-up gate and makes sure the schema exists. It must run before a scope begins and never inside one: the move
	/// of the old values that the gate runs needs the same scope gate. A failed schema is logged and the call goes on: the read or
	/// write that follows reports its own failure.
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
	/// While the one-time move into the database has not finished, removes the old copy of a value the user just changed or cleared,
	/// so that the next launch does not import the deleted or replaced data. The change itself is committed already, so a storage that
	/// cannot remove the old copy is logged and does not fail the call.
	/// </summary>
	/// <param name="remove">Removes the old copy.</param>
	/// <returns>A task that completes when the old copy is removed or left.</returns>
	private async Task RemoveOldCopyAsync(Func<Task> remove)
	{
		try
		{
			if (!await gate.IsMoveFinishedAsync().ConfigureAwait(false))
			{
				await remove().ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogOldCopyKept(logger, exception);
		}
	}

	/// <summary>
	/// Commits the staged changes of a scope.
	/// </summary>
	/// <param name="scope">The scope of the write.</param>
	/// <returns>A task that completes when the changes are saved.</returns>
	/// <exception cref="InvalidOperationException">The database refused the changes.</exception>
	private static async Task CommitAsync(IDeviceChangeScope scope) => ThrowIfFailed(await scope.CommitAsync().ConfigureAwait(false));

	/// <summary>
	/// Builds the exception for a stored value that exists but cannot be read, logs it and, when the failure is permanent, remembers it.
	/// A key that cannot be read now is temporary and is not remembered.
	/// </summary>
	/// <param name="what">What was read, for the log.</param>
	/// <param name="message">What the user is told.</param>
	/// <param name="failed">The failed read.</param>
	/// <param name="remember">Remembers the code of a permanent failure.</param>
	/// <returns>The exception to throw.</returns>
	private SavedDataUnreadableException Unreadable(string what, string message, Result failed, Action<string> remember)
	{
		var temporary = DeviceDataErrors.IsKeyUnavailable(failed);
		LogUnreadable(logger, what, failed.Error.Code);
		if (!temporary)
		{
			lock (cacheGate)
			{
				remember(failed.Error.Code);
			}
		}

		return new SavedDataUnreadableException(message, failed.Error.Code, temporary);
	}

	/// <summary>
	/// Forgets a remembered unreadable session, after the session was replaced or removed.
	/// </summary>
	private void ForgetUnreadableSession()
	{
		lock (cacheGate)
		{
			sessionUnreadableCode = null;
		}
	}

	/// <summary>
	/// Remembers the card for the next reads, and forgets that it was unreadable.
	/// </summary>
	/// <param name="card">The card, or <see langword="null"/> for none.</param>
	private void CacheCard(ParkingCard? card)
	{
		lock (cacheGate)
		{
			cachedCard = card;
			isCardCached = true;
			cardUnreadableCode = null;
		}
	}

	/// <summary>
	/// Turns a failed result into the exception that the callers of the old store already handled for a failed write.
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
