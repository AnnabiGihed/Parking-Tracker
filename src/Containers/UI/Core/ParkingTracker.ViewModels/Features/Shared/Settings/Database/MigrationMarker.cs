using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ParkingTracker.ViewModels.Features.Shared.Settings.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The bookkeeping of <see cref="SettingsToDatabaseMigration"/> in the preferences (ADR-0026): the marker
///              <c>pt.dbMigration</c> that says the move is over, and the count of failed attempts in <c>pt.dbMigrationAttempts</c>. A
///              finished move writes the marker with the outcome <c>migrated</c> or <c>nothing</c> and the keys it moved; the third
///              failed attempt writes it with the outcome <c>gaveUp</c>, so the user enters the card again. The marker is JSON with
///              <c>version</c>, <c>outcome</c>, <c>found</c> and <c>at</c>.
/// </summary>
internal sealed partial class MigrationMarker
{
	#region Constants
	/// <summary>
	/// The key of the marker that the move ran.
	/// </summary>
	internal const string Key = "pt.dbMigration";

	/// <summary>
	/// The key of the number of failed attempts so far.
	/// </summary>
	internal const string AttemptsKey = "pt.dbMigrationAttempts";

	/// <summary>
	/// How many launches may fail before the move gives up.
	/// </summary>
	internal const int MaxAttempts = 3;

	/// <summary>
	/// The version of the JSON of the marker.
	/// </summary>
	internal const int Version = 1;

	/// <summary>
	/// The marker outcome when values were moved.
	/// </summary>
	private const string MigratedName = "migrated";

	/// <summary>
	/// The marker outcome when there was nothing to move.
	/// </summary>
	private const string NothingName = "nothing";

	/// <summary>
	/// The marker outcome in the last allowed failed attempt.
	/// </summary>
	private const string GaveUpName = "gaveUp";
	#endregion

	#region Fields
	/// <summary>
	/// The preferences that hold the marker and the attempts.
	/// </summary>
	private readonly IKeyValueStorage plain;

	/// <summary>
	/// Clock for the time in the marker.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MigrationMarker"/> class.
	/// </summary>
	/// <param name="plain">The preferences that hold the marker and the attempts.</param>
	/// <param name="clock">Clock for the time in the marker.</param>
	/// <param name="logger">The log.</param>
	internal MigrationMarker(IKeyValueStorage plain, TimeProvider clock, ILogger logger)
	{
		this.plain = plain;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that an attempt failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="problem">What went wrong.</param>
	/// <param name="attempt">The number of this attempt.</param>
	/// <param name="maxAttempts">The number of attempts allowed.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The saved data could not be moved into the device database: {Problem} (attempt {Attempt} of {MaxAttempts})")]
	private static partial void LogAttemptFailed(ILogger logger, string problem, int attempt, int maxAttempts);

	/// <summary>
	/// Logs that the move finished.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="outcome">What the run did.</param>
	/// <param name="count">How many values were moved.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Moving the saved data into the device database finished: {Outcome}, {Count} value(s) moved")]
	private static partial void LogFinished(ILogger logger, DatabaseMigrationOutcome outcome, int count);
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the marker.
	/// </summary>
	/// <returns>A task with the JSON of the marker, or <see langword="null"/> while the move has not finished.</returns>
	internal Task<string?> ReadAsync() => plain.ReadAsync(Key);

	/// <summary>
	/// Records that the move finished.
	/// </summary>
	/// <param name="found">The keys that were moved; none when there was nothing to move.</param>
	/// <returns>A task with <see cref="DatabaseMigrationOutcome.Migrated"/> or <see cref="DatabaseMigrationOutcome.NothingFound"/>.</returns>
	internal async Task<DatabaseMigrationOutcome> FinishAsync(IReadOnlyList<string> found)
	{
		var outcome = found.Count > 0 ? DatabaseMigrationOutcome.Migrated : DatabaseMigrationOutcome.NothingFound;
		await WriteAsync(found.Count > 0 ? MigratedName : NothingName, found).ConfigureAwait(false);
		LogFinished(logger, outcome, found.Count);
		return outcome;
	}

	/// <summary>
	/// Counts a failed attempt; in the last allowed attempt the marker records the failure.
	/// </summary>
	/// <param name="problem">What went wrong.</param>
	/// <returns>A task with <see cref="DatabaseMigrationOutcome.WillRetry"/> or <see cref="DatabaseMigrationOutcome.GaveUp"/>.</returns>
	internal async Task<DatabaseMigrationOutcome> RecordFailureAsync(string problem)
	{
		var saved = await plain.ReadAsync(AttemptsKey).ConfigureAwait(false);
		var before = int.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : 0;
		var attempts = before + 1;
		await plain.WriteAsync(AttemptsKey, attempts.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
		LogAttemptFailed(logger, problem, attempts, MaxAttempts);
		if (attempts < MaxAttempts)
		{
			return DatabaseMigrationOutcome.WillRetry;
		}

		await WriteAsync(GaveUpName, []).ConfigureAwait(false);
		return DatabaseMigrationOutcome.GaveUp;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Writes the marker.
	/// </summary>
	/// <param name="outcome">The outcome name: <c>migrated</c>, <c>nothing</c> or <c>gaveUp</c>.</param>
	/// <param name="found">The keys that were moved.</param>
	/// <returns>A task that completes when the marker is saved.</returns>
	private async Task WriteAsync(string outcome, IReadOnlyList<string> found)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteNumber("version", Version);
			writer.WriteString("outcome", outcome);
			writer.WriteStartArray("found");
			foreach (var key in found)
			{
				writer.WriteStringValue(key);
			}

			writer.WriteEndArray();
			writer.WriteNumber("at", clock.GetUtcNow().ToUnixTimeMilliseconds());
			writer.WriteEndObject();
		}

		await plain.WriteAsync(Key, Encoding.UTF8.GetString(stream.ToArray())).ConfigureAwait(false);
	}
	#endregion
}
