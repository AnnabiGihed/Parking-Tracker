using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The one-time copy of what the Capacitor version saved (ADR-0004, ADR-0008). While the preference <c>pt.migration</c> is
///              absent it reads the old <c>pt.*</c> values through <see cref="ILegacyStorageReader"/>, checks each one with
///              <see cref="SettingsJson"/> (a value that does not read is skipped and logged, never fatal), saves the card through
///              the settings store (encrypted storage) and the other values to the plain preferences, and never replaces a value that
///              the new app already holds. The marker is written only after a successful read, so a failed read is tried again at the
///              next launch; every failed attempt is counted (a failed read, a value that cannot be saved, an unexpected exception and
///              a run that takes longer than <see cref="RunTimeout"/>), and after <see cref="MaxAttempts"/> failures the marker records
///              the failure and the user enters the card again. The whole run is bounded by <see cref="RunTimeout"/>, so the screens
///              that wait for it always get on. A run is shared by every caller of <see cref="RunOnceAsync"/>. The old data is never
///              deleted.
/// </summary>
public sealed partial class LegacyDataMigration
{
	#region Constants
	/// <summary>
	/// The key of the marker that the migration ran; its value is JSON with <c>version</c>, <c>outcome</c>, <c>reader</c>, <c>found</c> and <c>at</c>.
	/// </summary>
	public const string MarkerKey = "pt.migration";

	/// <summary>
	/// The key of the number of failed attempts so far.
	/// </summary>
	public const string AttemptsKey = "pt.migrationAttempts";

	/// <summary>
	/// How many launches may fail before the migration gives up.
	/// </summary>
	public const int MaxAttempts = 3;

	/// <summary>
	/// The version of the JSON of the marker.
	/// </summary>
	public const int MarkerVersion = 1;

	/// <summary>
	/// The marker outcome when values were copied.
	/// </summary>
	private const string MigratedName = "migrated";

	/// <summary>
	/// The marker outcome when the old storage held nothing usable.
	/// </summary>
	private const string NothingName = "nothing";

	/// <summary>
	/// The marker outcome when the old storage could not be read in the last allowed attempt.
	/// </summary>
	private const string GaveUpName = "gaveUp";
	#endregion

	#region Static Instances
	/// <summary>
	/// How long the whole run (read and import) may take; the app must not wait longer for data that is only a convenience. A
	/// longer run counts as a failed attempt.
	/// </summary>
	public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

	/// <summary>
	/// How long counting a failed attempt may take after the run timed out, so that a stuck storage cannot hold the start-up either.
	/// </summary>
	private static readonly TimeSpan CountTimeout = TimeSpan.FromSeconds(2);

	/// <summary>
	/// The keys that are copied to the plain preferences.
	/// </summary>
	private static readonly string[] PlainKeys =
	[
		SettingsJson.CarKey,
		SettingsJson.TimerKey,
		SettingsJson.AlertsKey,
		SettingsJson.LayersKey,
		SettingsJson.PermissionSnoozeKey,
	];
	#endregion

	#region Fields
	/// <summary>
	/// Reads the old storage.
	/// </summary>
	private readonly ILegacyStorageReader reader;

	/// <summary>
	/// The settings store, which receives the card.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// The plain preferences, which receive the other values and the marker.
	/// </summary>
	private readonly IKeyValueStorage plain;

	/// <summary>
	/// Clock for the time limit and the time in the marker.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<LegacyDataMigration> logger;

	/// <summary>
	/// Guards <see cref="run"/>.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The run of this process, shared by every caller.
	/// </summary>
	private Task<LegacyMigrationOutcome>? run;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LegacyDataMigration"/> class.
	/// </summary>
	/// <param name="reader">Reads the old storage.</param>
	/// <param name="settings">The settings store, which receives the card.</param>
	/// <param name="plain">The plain preferences, which receive the other values and the marker.</param>
	/// <param name="clock">Clock for the time limit and the time in the marker.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public LegacyDataMigration(ILegacyStorageReader reader, ISettingsStore settings, IKeyValueStorage plain, TimeProvider clock, ILogger<LegacyDataMigration> logger)
	{
		ArgumentNullException.ThrowIfNull(reader);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(plain);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.reader = reader;
		this.settings = settings;
		this.plain = plain;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a run that failed because the storage of the phone failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Migrating the saved data of the previous version failed")]
	private static partial void LogRunFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that an attempt to migrate failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="problem">What went wrong.</param>
	/// <param name="attempt">The number of this attempt.</param>
	/// <param name="maxAttempts">The number of attempts allowed.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The saved data of the previous version could not be migrated: {Problem} (attempt {Attempt} of {MaxAttempts})")]
	private static partial void LogAttemptFailed(ILogger logger, string problem, int attempt, int maxAttempts);

	/// <summary>
	/// Logs a value that was skipped because it did not read.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="key">The key of the value.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The old value {Key} is not readable and was skipped")]
	private static partial void LogSkippedUnreadable(ILogger logger, string key);

	/// <summary>
	/// Logs a value that was skipped because the new app already holds one.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="key">The key of the value.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "The old value {Key} was skipped: the new app already has one")]
	private static partial void LogSkippedExisting(ILogger logger, string key);

	/// <summary>
	/// Logs a value that could not be saved.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="key">The key of the value.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The old value {Key} could not be saved")]
	private static partial void LogImportFailed(ILogger logger, string key, Exception exception);

	/// <summary>
	/// Logs that a time-out could not be counted either.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The migration timed out and the failed attempt could not be counted; the next launch tries again")]
	private static partial void LogAttemptNotCounted(ILogger logger);

	/// <summary>
	/// Logs that the migration finished.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="outcome">What the run did.</param>
	/// <param name="count">How many values were copied.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Migration of the saved data finished: {Outcome}, {Count} value(s) copied")]
	private static partial void LogFinished(ILogger logger, LegacyMigrationOutcome outcome, int count);
	#endregion

	#region Public Methods
	/// <summary>
	/// Runs the migration once in this process; every later call returns the same run, so the caller can wait for it. It never
	/// throws: a problem is logged and shows in the outcome.
	/// </summary>
	/// <returns>A task with what the run did.</returns>
	public Task<LegacyMigrationOutcome> RunOnceAsync()
	{
		lock (gate)
		{
			return run ??= Task.Run(RunLimitedAsync);
		}
	}

	/// <summary>
	/// Reads the marker that records the outcome of the migration, for the upgrade test.
	/// </summary>
	/// <returns>A task with the JSON of the marker, or <see langword="null"/> while the migration has not finished.</returns>
	public Task<string?> ReadMarkerAsync() => plain.ReadAsync(MarkerKey);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Runs the migration within <see cref="RunTimeout"/>; a run that takes longer counts as a failed attempt and the caller gets on
	/// while the slow run may still finish in the background (it checks the limit between its steps).
	/// </summary>
	/// <returns>A task with what the run did.</returns>
	private async Task<LegacyMigrationOutcome> RunLimitedAsync()
	{
		using var limit = new CancellationTokenSource(RunTimeout, clock);
		try
		{
			return await RunAsync(limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return await RecordTimeoutAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Counts the time-out as a failed attempt, itself bounded by <see cref="CountTimeout"/>.
	/// </summary>
	/// <returns>A task with the outcome of the failed attempt.</returns>
	private async Task<LegacyMigrationOutcome> RecordTimeoutAsync()
	{
		try
		{
			return await FailSafelyAsync($"the migration took longer than {RunTimeout.TotalSeconds} seconds").WaitAsync(CountTimeout, clock).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			LogAttemptNotCounted(logger);
			return LegacyMigrationOutcome.WillRetry;
		}
	}

	/// <summary>
	/// Runs the migration: nothing when the marker exists, otherwise read, import and mark.
	/// </summary>
	/// <param name="cancellationToken">Ends the run when the time limit is reached.</param>
	/// <returns>A task with what the run did.</returns>
	private async Task<LegacyMigrationOutcome> RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (await plain.ReadAsync(MarkerKey).ConfigureAwait(false) is not null)
			{
				return LegacyMigrationOutcome.AlreadyDone;
			}

			var reading = await ReadLegacyAsync(cancellationToken).ConfigureAwait(false);
			return reading.Succeeded
				? await ImportAsync(reading, cancellationToken).ConfigureAwait(false)
				: await FailAsync(reading.Problem!).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// The storage of the phone itself failed; the app starts anyway, and the failure counts like any other.
			LogRunFailed(logger, exception);
			return await FailSafelyAsync($"{exception.GetType().Name}: {exception.Message}").ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Reads the old storage, turning an exception into a failed reading; a cancellation goes on to the caller.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read when the time limit is reached.</param>
	/// <returns>A task with the reading.</returns>
	private async Task<LegacyStorageReading> ReadLegacyAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return LegacyStorageReading.Failure($"{exception.GetType().Name}: {exception.Message}");
		}
	}

	/// <summary>
	/// Copies the usable values, then writes the marker unless a value could not be saved.
	/// </summary>
	/// <param name="reading">The successful reading of the old storage.</param>
	/// <param name="cancellationToken">Ends the import when the time limit is reached.</param>
	/// <returns>A task with what the run did.</returns>
	private async Task<LegacyMigrationOutcome> ImportAsync(LegacyStorageReading reading, CancellationToken cancellationToken)
	{
		var values = reading.Values;
		var found = new List<string>();
		var allSaved = true;
		foreach (var key in values.Keys.Where(IsKnownKey).Order(StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				if (await ImportValueAsync(key, values[key]).ConfigureAwait(false))
				{
					found.Add(key);
				}
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				LogImportFailed(logger, key, exception);
				allSaved = false;
			}
		}

		if (!allSaved)
		{
			return await FailAsync("a value could not be saved").ConfigureAwait(false);
		}

		var outcome = found.Count > 0 ? LegacyMigrationOutcome.Migrated : LegacyMigrationOutcome.NothingFound;
		await WriteMarkerAsync(found.Count > 0 ? MigratedName : NothingName, reading.Reader, found).ConfigureAwait(false);
		LogFinished(logger, outcome, found.Count);
		return outcome;
	}

	/// <summary>
	/// Copies one value unless it is unreadable or the new app already holds one.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <param name="json">The text the old version saved.</param>
	/// <returns>A task with <see langword="true"/> when the value was copied.</returns>
	private async Task<bool> ImportValueAsync(string key, string json)
	{
		if (!IsUsable(key, json))
		{
			LogSkippedUnreadable(logger, key);
			return false;
		}

		if (string.Equals(key, SettingsJson.CardKey, StringComparison.Ordinal))
		{
			return await ImportCardAsync(json).ConfigureAwait(false);
		}

		if (await plain.ReadAsync(key).ConfigureAwait(false) is not null)
		{
			LogSkippedExisting(logger, key);
			return false;
		}

		await plain.WriteAsync(key, json.Trim()).ConfigureAwait(false);
		return true;
	}

	/// <summary>
	/// Saves the card through the settings store, which keeps it in the encrypted storage, unless a card exists already.
	/// </summary>
	/// <param name="json">The text the old version saved under <c>pt.card</c>.</param>
	/// <returns>A task with <see langword="true"/> when the card was saved.</returns>
	private async Task<bool> ImportCardAsync(string json)
	{
		if (await settings.GetCardAsync().ConfigureAwait(false) is not null)
		{
			LogSkippedExisting(logger, SettingsJson.CardKey);
			return false;
		}

		await settings.SaveCardAsync(SettingsJson.ParseCard(json)!).ConfigureAwait(false);
		return true;
	}

	/// <summary>
	/// Records a failed attempt like <see cref="FailAsync"/>, and treats a failure of the counting itself as a retry.
	/// </summary>
	/// <param name="problem">What went wrong.</param>
	/// <returns>A task with <see cref="LegacyMigrationOutcome.WillRetry"/> or <see cref="LegacyMigrationOutcome.GaveUp"/>.</returns>
	private async Task<LegacyMigrationOutcome> FailSafelyAsync(string problem)
	{
		try
		{
			return await FailAsync(problem).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogRunFailed(logger, exception);
			return LegacyMigrationOutcome.WillRetry;
		}
	}

	/// <summary>
	/// Records a failed attempt; in the last allowed attempt the marker records the failure so that the user enters the card again.
	/// </summary>
	/// <param name="problem">What went wrong.</param>
	/// <returns>A task with <see cref="LegacyMigrationOutcome.WillRetry"/> or <see cref="LegacyMigrationOutcome.GaveUp"/>.</returns>
	private async Task<LegacyMigrationOutcome> FailAsync(string problem)
	{
		var attempts = await CountAttemptAsync().ConfigureAwait(false);
		LogAttemptFailed(logger, problem, attempts, MaxAttempts);
		if (attempts < MaxAttempts)
		{
			return LegacyMigrationOutcome.WillRetry;
		}

		await WriteMarkerAsync(GaveUpName, LegacyStorageReading.NoReader, []).ConfigureAwait(false);
		return LegacyMigrationOutcome.GaveUp;
	}

	/// <summary>
	/// Adds one to the number of failed attempts.
	/// </summary>
	/// <returns>A task with the new number.</returns>
	private async Task<int> CountAttemptAsync()
	{
		var saved = await plain.ReadAsync(AttemptsKey).ConfigureAwait(false);
		var before = int.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : 0;
		var attempts = before + 1;
		await plain.WriteAsync(AttemptsKey, attempts.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
		return attempts;
	}

	/// <summary>
	/// Writes the marker.
	/// </summary>
	/// <param name="outcome">The outcome name: <c>migrated</c>, <c>nothing</c> or <c>gaveUp</c>.</param>
	/// <param name="readerName">The name of the reader that found the values, or <c>none</c>.</param>
	/// <param name="found">The keys that were copied.</param>
	/// <returns>A task that completes when the marker is saved.</returns>
	private async Task WriteMarkerAsync(string outcome, string readerName, IReadOnlyList<string> found)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteNumber("version", MarkerVersion);
			writer.WriteString("outcome", outcome);
			writer.WriteString("reader", readerName);
			writer.WriteStartArray("found");
			foreach (var key in found)
			{
				writer.WriteStringValue(key);
			}

			writer.WriteEndArray();
			writer.WriteNumber("at", clock.GetUtcNow().ToUnixTimeMilliseconds());
			writer.WriteEndObject();
		}

		await plain.WriteAsync(MarkerKey, Encoding.UTF8.GetString(stream.ToArray())).ConfigureAwait(false);
	}

	/// <summary>
	/// Tells whether a key is one the migration copies.
	/// </summary>
	/// <param name="key">The key of the old storage.</param>
	/// <returns><see langword="true"/> for the card and the plain keys.</returns>
	private static bool IsKnownKey(string key)
	{
		return string.Equals(key, SettingsJson.CardKey, StringComparison.Ordinal) || PlainKeys.Contains(key, StringComparer.Ordinal);
	}

	/// <summary>
	/// Tells whether the old text reads as the value of its key, by the same parsers the app uses.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <param name="json">The text.</param>
	/// <returns><see langword="true"/> when the parser finds a value in the text.</returns>
	private static bool IsUsable(string key, string json)
	{
		return key switch
		{
			SettingsJson.CardKey => SettingsJson.ParseCard(json) is not null,
			SettingsJson.CarKey => SettingsJson.ParseCar(json) is not null,
			SettingsJson.TimerKey => SettingsJson.ParseTimer(json) is not null,
			SettingsJson.PermissionSnoozeKey => SettingsJson.ParsePermissionSnooze(json) is not null,
			SettingsJson.AlertsKey => json.Trim() is "true" or "false",
			SettingsJson.LayersKey => IsJsonObject(json),
			_ => false,
		};
	}

	/// <summary>
	/// Tells whether a text is a JSON object; the layer switches have no required field, so only the shape can be checked.
	/// </summary>
	/// <param name="json">The text.</param>
	/// <returns><see langword="true"/> for a JSON object.</returns>
	private static bool IsJsonObject(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}
	#endregion
}
