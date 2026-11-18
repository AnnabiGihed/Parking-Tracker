using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.ParkingSessions.Aggregates;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.Shared.Settings.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The one-time move of the card, the parked car and the timer from the settings (<c>pt.card</c> in the encrypted
///              storage, <c>pt.car</c> and <c>pt.timer</c> in the preferences) into the device database (ADR-0026). While the preference
///              <c>pt.dbMigration</c> is absent it reads the old values with the <see cref="SettingsJson"/> readers, builds the
///              aggregates (a value that does not read, or breaks a rule, is skipped and logged, never fatal), stages them in one
///              scope of the unit of work and reads them back; only when the database returns what was written is the marker written.
///              The scope holds the write gate from the first read to the read-back, so a write of the settings store cannot slip in
///              between, and a row that exists already is never overwritten. The old keys are never deleted in this release (a later
///              release removes them), so a failed or abandoned move loses nothing. Every failed attempt is counted by the
///              <see cref="MigrationMarker"/>; the third failure writes the marker with the outcome <c>gaveUp</c> and the user
///              enters the card again. A key that cannot be read now is not a failed attempt: the move is tried again at the next
///              launch and nothing is counted. The whole run is bounded by <see cref="RunTimeout"/>; work that goes on after the
///              time-out is safe because the next run finds what it wrote and skips it. A run is shared by every caller of
///              <see cref="RunOnceAsync"/>.
/// </summary>
public sealed partial class SettingsToDatabaseMigration
{
	#region Constants
	/// <summary>
	/// The key of the marker that the move ran; its value is JSON with <c>version</c>, <c>outcome</c>, <c>found</c> and <c>at</c>.
	/// </summary>
	public const string MarkerKey = MigrationMarker.Key;

	/// <summary>
	/// The key of the number of failed attempts so far.
	/// </summary>
	public const string AttemptsKey = MigrationMarker.AttemptsKey;

	/// <summary>
	/// How many launches may fail before the move gives up.
	/// </summary>
	public const int MaxAttempts = MigrationMarker.MaxAttempts;

	/// <summary>
	/// The version of the JSON of the marker.
	/// </summary>
	public const int MarkerVersion = MigrationMarker.Version;
	#endregion

	#region Static Instances
	/// <summary>
	/// How long the whole run may take; a longer run counts as a failed attempt.
	/// </summary>
	public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);
	#endregion

	#region Fields
	/// <summary>
	/// The encrypted storage that holds the old card.
	/// </summary>
	private readonly IKeyValueStorage secure;

	/// <summary>
	/// The preferences that hold the old car and timer.
	/// </summary>
	private readonly IKeyValueStorage plain;

	/// <summary>
	/// The marker and the count of failed attempts.
	/// </summary>
	private readonly MigrationMarker marker;

	/// <summary>
	/// The repository of the residents' card.
	/// </summary>
	private readonly IResidentCardRepository cards;

	/// <summary>
	/// The repository of the parking session.
	/// </summary>
	private readonly IParkingSessionRepository sessions;

	/// <summary>
	/// Begins the scope in which the move stages and commits its changes.
	/// </summary>
	private readonly IDeviceUnitOfWork unitOfWork;

	/// <summary>
	/// The municipalities that the old card is checked against.
	/// </summary>
	private readonly IReferenceData referenceData;

	/// <summary>
	/// Clock for the time limit.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<SettingsToDatabaseMigration> logger;

	/// <summary>
	/// Guards <see cref="run"/>.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The run of this process, shared by every caller.
	/// </summary>
	private Task<DatabaseMigrationOutcome>? run;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SettingsToDatabaseMigration"/> class.
	/// </summary>
	/// <param name="secure">The encrypted storage that holds the old card.</param>
	/// <param name="plain">The preferences that hold the old car and timer, the marker and the attempts.</param>
	/// <param name="cards">The repository of the residents' card.</param>
	/// <param name="sessions">The repository of the parking session.</param>
	/// <param name="unitOfWork">Begins the scope in which the move stages and commits its changes.</param>
	/// <param name="referenceData">The municipalities that the old card is checked against.</param>
	/// <param name="clock">Clock for the time limit and the time in the marker.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public SettingsToDatabaseMigration(
		IKeyValueStorage secure,
		IKeyValueStorage plain,
		IResidentCardRepository cards,
		IParkingSessionRepository sessions,
		IDeviceUnitOfWork unitOfWork,
		IReferenceData referenceData,
		TimeProvider clock,
		ILogger<SettingsToDatabaseMigration> logger)
	{
		ArgumentNullException.ThrowIfNull(secure);
		ArgumentNullException.ThrowIfNull(plain);
		ArgumentNullException.ThrowIfNull(cards);
		ArgumentNullException.ThrowIfNull(sessions);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(referenceData);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.secure = secure;
		this.plain = plain;
		this.cards = cards;
		this.sessions = sessions;
		this.unitOfWork = unitOfWork;
		this.referenceData = referenceData;
		this.clock = clock;
		this.logger = logger;
		marker = new MigrationMarker(plain, clock, logger);
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a run that failed because the storage or the database failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Moving the saved data into the device database failed")]
	private static partial void LogRunFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs an old value that was skipped.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="key">The key of the old value.</param>
	/// <param name="reason">Why it was skipped.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The old value {Key} was not moved: {Reason}")]
	private static partial void LogSkipped(ILogger logger, string key, string reason);

	/// <summary>
	/// Logs a move that waits for the next launch because the key cannot be read now; no attempt is counted.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="problem">What could not be done.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The saved data is not moved yet: {Problem}; it is tried again at the next launch")]
	private static partial void LogWaiting(ILogger logger, string problem);
	#endregion

	#region Public Methods
	/// <summary>
	/// Runs the move once in this process; every later call returns the same run. It never throws: a problem is logged and shows in
	/// the outcome. The device database must be initialized before.
	/// </summary>
	/// <returns>A task with what the run did.</returns>
	public Task<DatabaseMigrationOutcome> RunOnceAsync()
	{
		lock (gate)
		{
			return run ??= Task.Run(RunLimitedAsync);
		}
	}

	/// <summary>
	/// Reads the marker that records the outcome of the move, for the upgrade test.
	/// </summary>
	/// <returns>A task with the JSON of the marker, or <see langword="null"/> while the move has not finished.</returns>
	public Task<string?> ReadMarkerAsync() => marker.ReadAsync();
	#endregion

	#region Private Helpers
	/// <summary>
	/// Runs the move within <see cref="RunTimeout"/>; a run that takes longer counts as a failed attempt.
	/// </summary>
	/// <returns>A task with what the run did.</returns>
	private async Task<DatabaseMigrationOutcome> RunLimitedAsync()
	{
		using var limit = new CancellationTokenSource(RunTimeout, clock);
		try
		{
			return await RunAsync(limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return await FailSafelyAsync($"the move took longer than {RunTimeout.TotalSeconds} seconds").ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Runs the move: nothing when the marker exists, otherwise read, stage, commit, read back and mark.
	/// </summary>
	/// <param name="cancellationToken">Ends the run when the time limit is reached.</param>
	/// <returns>A task with what the run did.</returns>
	private async Task<DatabaseMigrationOutcome> RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (await marker.ReadAsync().ConfigureAwait(false) is not null)
			{
				return DatabaseMigrationOutcome.AlreadyDone;
			}

			var (found, problem) = await MoveAsync(cancellationToken).ConfigureAwait(false);
			return problem is null ? await marker.FinishAsync(found).ConfigureAwait(false) : await FailAsync(problem).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogRunFailed(logger, exception);
			return await FailSafelyAsync($"{exception.GetType().Name}: {exception.Message}").ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Plans and applies the move while holding the write scope of the database.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wait for the scope and the reads.</param>
	/// <returns>A task with the old keys that were moved, and the problem that stopped the move, or <see langword="null"/>.</returns>
	private async Task<(IReadOnlyList<string> Found, MigrationProblem? Problem)> MoveAsync(CancellationToken cancellationToken)
	{
		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var plan = await PlanAsync(cancellationToken).ConfigureAwait(false);
			if (plan.Problem is not null)
			{
				return (plan.Found, plan.Problem);
			}

			return (plan.Found, await ApplyAsync(scope, plan, cancellationToken).ConfigureAwait(false));
		}
	}

	/// <summary>
	/// Reads the old values and builds the aggregates that are still missing in the database.
	/// </summary>
	/// <param name="cancellationToken">Cancels the reads.</param>
	/// <returns>A task with the plan; its problem is set when the database could not be read.</returns>
	private async Task<MigrationPlan> PlanAsync(CancellationToken cancellationToken)
	{
		var storedCard = await cards.GetAsync(cancellationToken).ConfigureAwait(false);
		var storedSession = await sessions.GetAsync(cancellationToken).ConfigureAwait(false);
		var unreadable = UnreadableProblem(storedCard) ?? UnreadableProblem(storedSession);
		if (unreadable is not null)
		{
			return new MigrationPlan(null, null, [], unreadable);
		}

		var found = new List<string>();
		var card = storedCard.IsFailure ? await BuildCardAsync().ConfigureAwait(false) : null;
		if (card is not null)
		{
			found.Add(SettingsJson.CardKey);
		}

		var session = storedSession.IsFailure ? await BuildSessionAsync(found).ConfigureAwait(false) : null;
		return new MigrationPlan(card, session, found, null);
	}

	/// <summary>
	/// Tells why a read of the database stops the move: a key that cannot be read now waits, anything but "nothing stored" counts
	/// as a failed attempt.
	/// </summary>
	/// <param name="read">The result of reading the card or the session.</param>
	/// <returns>The problem, or <see langword="null"/> when the value was read or nothing is stored.</returns>
	private static MigrationProblem? UnreadableProblem(Result read)
	{
		if (DeviceDataErrors.IsKeyUnavailable(read))
		{
			return MigrationProblem.KeyNotReadable;
		}

		return read.IsFailure && !DeviceDataErrors.IsNotStored(read) ? MigrationProblem.Failed("the device database cannot be read") : null;
	}

	/// <summary>
	/// Builds the card aggregate from the old <c>pt.card</c> value.
	/// </summary>
	/// <returns>A task with the card, or <see langword="null"/> when there is none or it does not read or breaks a rule.</returns>
	private async Task<ResidentCard?> BuildCardAsync()
	{
		var old = SettingsJson.ParseCard(await secure.ReadAsync(SettingsJson.CardKey).ConfigureAwait(false));
		if (old is null)
		{
			return null;
		}

		var data = referenceData.GetReferenceData();
		var municipality = data.IsSuccess ? data.Value.Municipalities.FirstOrDefault(candidate => string.Equals(candidate.Id, old.Municipality, StringComparison.Ordinal)) : null;
		if (municipality is null)
		{
			LogSkipped(logger, SettingsJson.CardKey, "the municipality of the card is not in the reference data");
			return null;
		}

		var created = ResidentCard.Create(municipality, old);
		if (created.IsFailure)
		{
			LogSkipped(logger, SettingsJson.CardKey, created.Error.Code);
			return null;
		}

		return created.Value;
	}

	/// <summary>
	/// Builds the session aggregate from the old <c>pt.car</c> and <c>pt.timer</c> values; a timer that does not fit the car stays in
	/// the preferences, where the settings store still reads it.
	/// </summary>
	/// <param name="found">The keys that are moved; receives <c>pt.car</c>, and <c>pt.timer</c> when the timer goes with the car.</param>
	/// <returns>A task with the session, or <see langword="null"/> when there is no car or it does not read or breaks a rule.</returns>
	private async Task<ParkingSession?> BuildSessionAsync(List<string> found)
	{
		var car = SettingsJson.ParseCar(await plain.ReadAsync(SettingsJson.CarKey).ConfigureAwait(false));
		if (car is null)
		{
			return null;
		}

		var timer = SettingsJson.ParseTimer(await plain.ReadAsync(SettingsJson.TimerKey).ConfigureAwait(false));
		var created = ParkingSession.FromParkedCar(car, timer);
		if (created.IsFailure && timer is not null)
		{
			LogSkipped(logger, SettingsJson.TimerKey, created.Error.Code);
			timer = null;
			created = ParkingSession.FromParkedCar(car, null);
		}

		if (created.IsFailure)
		{
			LogSkipped(logger, SettingsJson.CarKey, created.Error.Code);
			return null;
		}

		found.Add(SettingsJson.CarKey);
		if (timer is not null)
		{
			found.Add(SettingsJson.TimerKey);
		}

		return created.Value;
	}

	/// <summary>
	/// Stages the plan, commits it and reads it back; what does not match is removed again.
	/// </summary>
	/// <param name="scope">The scope that holds the write gate.</param>
	/// <param name="plan">What to write.</param>
	/// <param name="cancellationToken">Cancels the writes.</param>
	/// <returns>A task with <see langword="null"/> when the database holds what was written, otherwise what went wrong.</returns>
	private async Task<MigrationProblem?> ApplyAsync(IDeviceChangeScope scope, MigrationPlan plan, CancellationToken cancellationToken)
	{
		if (plan.Card is null && plan.Session is null)
		{
			return null;
		}

		var staged = plan.Card is null ? Result.Success() : await cards.AddAsync(scope, plan.Card, cancellationToken).ConfigureAwait(false);
		if (staged.IsFailure)
		{
			return DeviceDataErrors.IsKeyUnavailable(staged) ? MigrationProblem.KeyNotReadable : MigrationProblem.Failed("the card could not be prepared for the database");
		}

		staged = plan.Session is null ? Result.Success() : await sessions.AddAsync(scope, plan.Session, cancellationToken).ConfigureAwait(false);
		if (staged.IsFailure)
		{
			return DeviceDataErrors.IsKeyUnavailable(staged) ? MigrationProblem.KeyNotReadable : MigrationProblem.Failed("the parked car could not be prepared for the database");
		}

		if ((await scope.CommitAsync(cancellationToken).ConfigureAwait(false)).IsFailure)
		{
			return MigrationProblem.Failed("the database refused the values");
		}

		var mismatch = await ReadBackAsync(plan, cancellationToken).ConfigureAwait(false);
		if (mismatch is not null)
		{
			await RollBackAsync(scope, plan, cancellationToken).ConfigureAwait(false);
		}

		return mismatch;
	}

	/// <summary>
	/// Reads the written aggregates back and compares them with what was written, in the JSON shape of the settings.
	/// </summary>
	/// <param name="plan">What was written.</param>
	/// <param name="cancellationToken">Cancels the reads.</param>
	/// <returns>A task with <see langword="null"/> when every written value reads back equal, otherwise the problem.</returns>
	private async Task<MigrationProblem?> ReadBackAsync(MigrationPlan plan, CancellationToken cancellationToken)
	{
		if (plan.Card is not null)
		{
			var read = await cards.GetAsync(cancellationToken).ConfigureAwait(false);
			if (read.IsFailure || !string.Equals(SettingsJson.SerializeCard(read.Value.ToParkingCard()), SettingsJson.SerializeCard(plan.Card.ToParkingCard()), StringComparison.Ordinal))
			{
				return Mismatch(read);
			}
		}

		if (plan.Session is not null)
		{
			var read = await sessions.GetAsync(cancellationToken).ConfigureAwait(false);
			if (read.IsFailure || !string.Equals(SessionJson(read.Value), SessionJson(plan.Session), StringComparison.Ordinal))
			{
				return Mismatch(read);
			}
		}

		return null;
	}

	/// <summary>
	/// Tells what a read-back that did not match means.
	/// </summary>
	/// <param name="read">The result of the read-back.</param>
	/// <returns>A key that cannot be read now waits; anything else counts as a failed attempt.</returns>
	private static MigrationProblem Mismatch(Result read)
	{
		return DeviceDataErrors.IsKeyUnavailable(read) ? MigrationProblem.KeyNotReadable : MigrationProblem.Failed("the database returned other values than were written");
	}

	/// <summary>
	/// Writes the car and the timer of a session in the JSON of the settings, for the comparison.
	/// </summary>
	/// <param name="session">The session.</param>
	/// <returns>The two texts, joined.</returns>
	private static string SessionJson(ParkingSession session)
	{
		var timer = session.Timer is null ? string.Empty : SettingsJson.SerializeTimer(session.Timer);
		return SettingsJson.SerializeCar(session.ToParkedCar()) + timer;
	}

	/// <summary>
	/// Removes what a failed read-back found wrong, so that the next attempt starts clean. The scope still holds the write gate, so
	/// only the rows of this move are removed. A failure to do so is only logged: the next run then finds the rows and skips them.
	/// </summary>
	/// <param name="scope">The scope that holds the write gate.</param>
	/// <param name="plan">What was written.</param>
	/// <param name="cancellationToken">Cancels the commit.</param>
	/// <returns>A task that completes when the rows are removed.</returns>
	private async Task RollBackAsync(IDeviceChangeScope scope, MigrationPlan plan, CancellationToken cancellationToken)
	{
		if (plan.Card is not null)
		{
			cards.Remove(scope);
		}

		if (plan.Session is not null)
		{
			sessions.Remove(scope);
		}

		if ((await scope.CommitAsync(cancellationToken).ConfigureAwait(false)).IsFailure)
		{
			LogSkipped(logger, MigrationMarker.Key, "the values that did not read back equal could not be removed");
		}
	}

	/// <summary>
	/// Records a failed attempt like <see cref="FailAsync"/>, and treats a failure of the counting itself as a retry.
	/// </summary>
	/// <param name="problem">What went wrong.</param>
	/// <returns>A task with <see cref="DatabaseMigrationOutcome.WillRetry"/> or <see cref="DatabaseMigrationOutcome.GaveUp"/>.</returns>
	private async Task<DatabaseMigrationOutcome> FailSafelyAsync(string problem)
	{
		try
		{
			return await marker.RecordFailureAsync(problem).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogRunFailed(logger, exception);
			return DatabaseMigrationOutcome.WillRetry;
		}
	}

	/// <summary>
	/// Ends a run that did not finish the move: a key that cannot be read now only waits for the next launch, any other problem is
	/// counted as a failed attempt.
	/// </summary>
	/// <param name="problem">What went wrong.</param>
	/// <returns>A task with <see cref="DatabaseMigrationOutcome.WillRetry"/> or <see cref="DatabaseMigrationOutcome.GaveUp"/>.</returns>
	private async Task<DatabaseMigrationOutcome> FailAsync(MigrationProblem problem)
	{
		if (!problem.CountsAsAttempt)
		{
			LogWaiting(logger, problem.Text);
			return DatabaseMigrationOutcome.WillRetry;
		}

		return await marker.RecordFailureAsync(problem.Text).ConfigureAwait(false);
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : What the run is going to write: the aggregates that are missing in the database, the old keys they come from,
	///              and the problem that stops the run before it writes.
	/// </summary>
	/// <param name="Card">The card to add, or <see langword="null"/>.</param>
	/// <param name="Session">The session to add, or <see langword="null"/>.</param>
	/// <param name="Found">The old keys that are moved.</param>
	/// <param name="Problem">The problem that stops the run, or <see langword="null"/>.</param>
	private sealed record MigrationPlan(ResidentCard? Card, ParkingSession? Session, IReadOnlyList<string> Found, MigrationProblem? Problem);

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Why a run stopped before the move was finished, and whether it counts towards giving up.
	/// </summary>
	/// <param name="Text">What went wrong, for the log.</param>
	/// <param name="CountsAsAttempt">Whether the run counts towards giving up; a key that cannot be read now does not.</param>
	private sealed record MigrationProblem(string Text, bool CountsAsAttempt)
	{
		#region Static Instances
		/// <summary>
		/// Gets the problem of a key that cannot be read now: the move waits for the next launch and counts nothing.
		/// </summary>
		public static MigrationProblem KeyNotReadable { get; } = new("the key of the device database cannot be read now", false);
		#endregion

		#region Factory Methods
		/// <summary>
		/// Creates the problem of a step that failed and counts as an attempt.
		/// </summary>
		/// <param name="text">What went wrong.</param>
		/// <returns>The problem.</returns>
		public static MigrationProblem Failed(string text) => new(text, true);
		#endregion
	}
	#endregion
}
