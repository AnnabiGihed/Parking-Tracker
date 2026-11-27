using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Keeps the profile of the phone and the profile of the account in step (ADR-0032). One sync runs at a time and a caller
///              that arrives meanwhile joins it. It runs only when the data on the phone is known to belong to the signed-in account:
///              <see cref="OwnerVerification"/> holds the hash of the account read from the API in this run and it must equal the hash
///              stored on the phone, otherwise nothing is sent and no call is made. A round reads the local profile, reads the server
///              copy with <c>If-None-Match</c> of the base revision, and then: on 304 uploads when the phone changed; on 200 applies the
///              server copy when the phone did not change, or merges (<see cref="ProfileMerge"/>), applies the merge and uploads it
///              when it differs from the server copy; on 404 creates the profile when the phone holds a section. A stale revision (412)
///              starts a new round, at most three rounds in all. A change made while a round ran is never overwritten and keeps the
///              changed flag, so another round follows. A caller that asks while a sync runs makes it go through one more pass when it ends, and a
///              round that wrote nothing reads the changed flag again before it reports synced. Writing to the phone also names the account
///              the sync was verified for, so that a sign-out or a change of account during a round writes nothing. A refusal (400, 413, 428) is logged by its status and stops the sync: the same
///              document would be refused again. A network failure keeps the changes and reports <see cref="ProfileSyncState.Offline"/>.
///              Nothing the sync logs contains a value of the profile.
/// </summary>
public sealed partial class ProfileSyncService : IProfileSync
{
	#region Constants
	/// <summary>
	/// The most rounds one sync runs: enough to follow a writer on another phone, not enough to loop on a refusal.
	/// </summary>
	public const int MaximumRounds = 3;
	#endregion

	#region Fields
	/// <summary>
	/// The profile endpoints of the API.
	/// </summary>
	private readonly IProfileClient client;

	/// <summary>
	/// The local profile.
	/// </summary>
	private readonly IProfileLocalStore local;

	/// <summary>
	/// The account the data on the phone is bound to.
	/// </summary>
	private readonly IDeviceOwnerStore owner;

	/// <summary>
	/// Whether the bound account is the signed-in one.
	/// </summary>
	private readonly OwnerVerification verification;

	/// <summary>
	/// Makes the screens show data that came from the account.
	/// </summary>
	private readonly IScreenReloader screens;

	/// <summary>
	/// The start-up gate: the old data is moved into the database before the first sync reads it.
	/// </summary>
	private readonly IStartupGate gate;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<ProfileSyncService> logger;

	/// <summary>
	/// Guards <see cref="running"/>.
	/// </summary>
	private readonly Lock runGate = new();

	/// <summary>
	/// The sync that is running, which later callers join; <see langword="null"/> when none runs.
	/// </summary>
	private Task<ProfileSyncResult>? running;

	/// <summary>
	/// Whether a caller asked for a sync while one was running, so that the run goes through one more pass when it ends.
	/// </summary>
	private bool requestedAgain;

	/// <summary>
	/// The state as the number of <see cref="ProfileSyncState"/>, changed only through <see cref="Interlocked"/>.
	/// </summary>
	private int state = (int)ProfileSyncState.Pending;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileSyncService"/> class.
	/// </summary>
	/// <param name="client">The profile endpoints of the API.</param>
	/// <param name="local">The local profile.</param>
	/// <param name="owner">The account the data on the phone is bound to.</param>
	/// <param name="verification">Whether the bound account is the signed-in one.</param>
	/// <param name="screens">Makes the screens show data that came from the account.</param>
	/// <param name="gate">The start-up gate.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ProfileSyncService(IProfileClient client, IProfileLocalStore local, IDeviceOwnerStore owner, OwnerVerification verification, IScreenReloader screens, IStartupGate gate, ILogger<ProfileSyncService> logger)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(local);
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(verification);
		ArgumentNullException.ThrowIfNull(screens);
		ArgumentNullException.ThrowIfNull(gate);
		ArgumentNullException.ThrowIfNull(logger);
		this.client = client;
		this.local = local;
		this.owner = owner;
		this.verification = verification;
		this.screens = screens;
		this.gate = gate;
		this.logger = logger;
	}
	#endregion

	#region Events
	/// <inheritdoc />
	public event EventHandler? Changed;
	#endregion

	#region Properties
	/// <inheritdoc />
	public ProfileSyncState State => (ProfileSyncState)Volatile.Read(ref state);
	#endregion

	#region Logging
	/// <summary>
	/// Logs a call or a read that failed, by its code.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The profile sync stopped ({Code})")]
	private static partial void LogStopped(ILogger logger, string code);

	/// <summary>
	/// Logs a refusal of the server that a retry would not change.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="status">The HTTP status.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The server refused the profile ({Status}); the changes stay on the phone")]
	private static partial void LogRejected(ILogger logger, int status);

	/// <summary>
	/// Logs a failure nobody expected, by its type.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="kind">The type of the exception.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The profile sync failed unexpectedly ({Kind})")]
	private static partial void LogUnexpected(ILogger logger, string kind);

	/// <summary>
	/// Logs screens that could not be refreshed after the sync wrote data.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="kind">The type of the exception.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The screens could not be refreshed after the profile sync ({Kind})")]
	private static partial void LogReloadFailed(ILogger logger, string kind);
	#endregion

	#region IProfileSync Implementation
	/// <inheritdoc />
	public Task<ProfileSyncResult> SyncAsync(CancellationToken cancellationToken = default)
	{
		Task<ProfileSyncResult> shared;
		lock (runGate)
		{
			if (running is null)
			{
				// Started on the pool so that the task is stored before it can finish and clear the slot.
				running = Task.Run(RunAndReleaseAsync, CancellationToken.None);
			}
			else
			{
				// A change may have landed after the pass in progress read the phone, so the run goes through once more when it ends.
				requestedAgain = true;
			}

			shared = running;
		}

		return shared.WaitAsync(cancellationToken);
	}

	/// <inheritdoc />
	public void Request() => _ = SyncAsync();

	/// <inheritdoc />
	public void Reset() => SetState(ProfileSyncState.Pending);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gives the hash of the account that is verified right now. The store calls it under its write gate, so a sign-out that landed after the
	/// sync read the hash is seen before anything is written.
	/// </summary>
	/// <returns>The hash, or <see langword="null"/> when no account is verified.</returns>
	private string? CurrentOwnerHash() => verification.VerifiedHash;

	/// <summary>
	/// Runs a pass, and another one for as long as callers asked for a sync while it ran, and then frees the slot. The decision to stop and
	/// the freeing of the slot happen under one lock, so that a caller either sets the flag in time or starts the next run.
	/// </summary>
	/// <returns>A task with the result of the last pass.</returns>
	private async Task<ProfileSyncResult> RunAndReleaseAsync()
	{
		var finished = false;
		try
		{
			while (true)
			{
				var result = await RunAsync().ConfigureAwait(false);
				lock (runGate)
				{
					if (!requestedAgain)
					{
						running = null;
						finished = true;
						return result;
					}

					requestedAgain = false;
				}
			}
		}
		finally
		{
			if (!finished)
			{
				lock (runGate)
				{
					running = null;
					requestedAgain = false;
				}
			}
		}
	}

	/// <summary>
	/// Runs the sync when the account owns the data on the phone.
	/// </summary>
	/// <returns>A task with the state the sync ended in, or a result that says no sync ran.</returns>
	private async Task<ProfileSyncResult> RunAsync()
	{
		if (!await IsOwnerVerifiedAsync().ConfigureAwait(false))
		{
			return ProfileSyncResult.NotRun(State);
		}

		SetState(ProfileSyncState.Syncing);
		ProfileSyncState result;
		try
		{
			await gate.RunOnceAsync().ConfigureAwait(false);
			result = await RunRoundsAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogUnexpected(logger, exception.GetType().Name);
			result = ProfileSyncState.Error;
		}

		// A sign-out during the run reset the state; the result of a run for an account that is gone is not shown.
		if (verification.IsVerified)
		{
			SetState(result);
		}

		return new ProfileSyncResult(true, result);
	}

	/// <summary>
	/// Tells whether the hash bound on the phone is the hash of the account the app read from the API in this run.
	/// </summary>
	/// <returns>A task with <see langword="true"/> when the data belongs to the signed-in account.</returns>
	private async Task<bool> IsOwnerVerifiedAsync()
	{
		var verified = verification.VerifiedHash;
		if (verified is null)
		{
			return false;
		}

		var bound = await owner.GetAccountHashAsync().ConfigureAwait(false);
		return bound.IsSuccess && string.Equals(bound.Value, verified, StringComparison.Ordinal);
	}

	/// <summary>
	/// Runs rounds until one ends or the rounds are used up.
	/// </summary>
	/// <returns>A task with the state the sync ends in.</returns>
	private async Task<ProfileSyncState> RunRoundsAsync()
	{
		var outcome = Round.Raced;
		for (var round = 0; round < MaximumRounds; round++)
		{
			if (round > 0 && !await IsOwnerVerifiedAsync().ConfigureAwait(false))
			{
				// The account signed out or the phone was bound to another one while the rounds ran.
				return ProfileSyncState.Pending;
			}

			outcome = await RoundAsync().ConfigureAwait(false);
			if (outcome is not (Round.Stale or Round.Raced))
			{
				break;
			}
		}

		return outcome switch
		{
			Round.Synced => ProfileSyncState.Synced,
			Round.Offline => ProfileSyncState.Offline,

			// Still changing while the rounds ran: a caller that arrived meanwhile has set the flag that gives the run another pass, so
			// this is waiting, not failing.
			Round.Raced => ProfileSyncState.Pending,
			_ => ProfileSyncState.Error,
		};
	}

	/// <summary>
	/// Runs one round: read the phone, read the server, and settle the difference.
	/// </summary>
	/// <returns>A task with how the round ended.</returns>
	private async Task<Round> RoundAsync()
	{
		var read = await local.ReadAsync().ConfigureAwait(false);
		if (read.IsFailure)
		{
			return Failed(read.Error);
		}

		var snapshot = read.Value;
		var got = await client.GetAsync(snapshot.State.BaseRevision).ConfigureAwait(false);
		if (got.IsFailure)
		{
			return Failed(got.Error);
		}

		return got.Value.Outcome switch
		{
			ProfileGetOutcome.NotModified => await SettleNotModifiedAsync(snapshot).ConfigureAwait(false),
			ProfileGetOutcome.Found => await SettleFoundAsync(snapshot, got.Value.Revision, got.Value.Document!).ConfigureAwait(false),
			_ => await SettleMissingAsync(snapshot).ConfigureAwait(false),
		};
	}

	/// <summary>
	/// Settles the case where the server copy is the one the phone holds: a phone that changed uploads, a phone that did not is checked once more.
	/// </summary>
	/// <param name="snapshot">The local profile.</param>
	/// <returns>A task with how the round ended.</returns>
	private async Task<Round> SettleNotModifiedAsync(ProfileSnapshot snapshot)
	{
		return snapshot.State.IsDirty
			? await UploadAsync(snapshot.Document, snapshot.State.Times, snapshot.State.BaseRevision).ConfigureAwait(false)
			: await ConfirmCleanAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Reads the changed flag once more after a round that wrote nothing, so that a change that landed after the snapshot is never reported as
	/// synced.
	/// </summary>
	/// <returns>A task with <see cref="Round.Synced"/> when the flag is still clear, <see cref="Round.Raced"/> when it is set.</returns>
	private async Task<Round> ConfirmCleanAsync()
	{
		var read = await local.ReadAsync().ConfigureAwait(false);
		if (read.IsFailure)
		{
			return Failed(read.Error);
		}

		return read.Value.State.IsDirty ? Round.Raced : Round.Synced;
	}

	/// <summary>
	/// Settles a server document against the phone: a plain download when the phone did not change, a merge otherwise.
	/// </summary>
	/// <param name="snapshot">The local profile.</param>
	/// <param name="revision">The revision of the server document.</param>
	/// <param name="server">The server document.</param>
	/// <returns>A task with how the round ended.</returns>
	private async Task<Round> SettleFoundAsync(ProfileSnapshot snapshot, long revision, ProfileDocument server)
	{
		if (snapshot.State.BaseRevision is not null && !snapshot.State.IsDirty)
		{
			return await ApplyAsync(snapshot, server, ProfileMerge.TimesOf(server), revision, needsUpload: false).ConfigureAwait(false);
		}

		var merged = ProfileMerge.Merge(snapshot, server);
		var needsUpload = !ProfileMerge.AreSame(merged.Document, server);
		var applied = await ApplyAsync(snapshot, merged.Document, merged.Times, revision, needsUpload).ConfigureAwait(false);
		if (applied != Round.Synced || !needsUpload)
		{
			return applied;
		}

		return await UploadAsync(merged.Document, merged.Times, revision).ConfigureAwait(false);
	}

	/// <summary>
	/// Settles the case where the server holds no document: the phone creates it when it holds a section.
	/// </summary>
	/// <param name="snapshot">The local profile.</param>
	/// <returns>A task with how the round ended.</returns>
	private async Task<Round> SettleMissingAsync(ProfileSnapshot snapshot)
	{
		if (!snapshot.IsEmpty)
		{
			return await UploadAsync(snapshot.Document, snapshot.State.Times, null).ConfigureAwait(false);
		}

		// Nothing to create, so a changed flag left by clearing everything has nothing to wait for.
		return snapshot.State.IsDirty ? await MarkSavedAsync(null, snapshot.State.Times).ConfigureAwait(false) : await ConfirmCleanAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Writes a document to the phone, and tells the screens when a section differs.
	/// </summary>
	/// <param name="snapshot">The local profile the document was derived from.</param>
	/// <param name="document">The document to hold.</param>
	/// <param name="times">The moment of each section after the write.</param>
	/// <param name="revision">The revision of the server document.</param>
	/// <param name="needsUpload">Whether the document still has to be uploaded.</param>
	/// <returns>A task with <see cref="Round.Synced"/> when it was written, <see cref="Round.Raced"/> when the user changed something meanwhile.</returns>
	private async Task<Round> ApplyAsync(ProfileSnapshot snapshot, ProfileDocument document, SectionTimes times, long revision, bool needsUpload)
	{
		var applied = await local.ApplyAsync(snapshot, document, times, revision, needsUpload, CurrentOwnerHash).ConfigureAwait(false);
		if (applied.IsFailure)
		{
			return Failed(applied.Error);
		}

		if (!applied.Value)
		{
			return Round.Raced;
		}

		if (Enum.GetValues<SyncSection>().Any(section => !ProfileMerge.SameData(section, snapshot.Document, document)))
		{
			await ReloadScreensAsync().ConfigureAwait(false);
		}

		return Round.Synced;
	}

	/// <summary>
	/// Uploads a document and records the revision the server answered.
	/// </summary>
	/// <param name="document">The document.</param>
	/// <param name="times">The moment of each section of the document.</param>
	/// <param name="ifMatchRevision">The revision the document replaces, or <see langword="null"/> to create.</param>
	/// <returns>A task with how the round ended.</returns>
	private async Task<Round> UploadAsync(ProfileDocument document, SectionTimes times, long? ifMatchRevision)
	{
		var put = await client.PutAsync(document, ifMatchRevision).ConfigureAwait(false);
		if (put.IsFailure)
		{
			return Failed(put.Error);
		}

		switch (put.Value.Outcome)
		{
			case ProfilePutOutcome.Saved:
				return await MarkSavedAsync(put.Value.Revision, times).ConfigureAwait(false);
			case ProfilePutOutcome.Stale:
				return Round.Stale;
			default:
				LogRejected(logger, put.Value.Status);
				return Round.Failed;
		}
	}

	/// <summary>
	/// Records on the phone that the server holds what was uploaded.
	/// </summary>
	/// <param name="revision">The revision the server answered, or <see langword="null"/>.</param>
	/// <param name="uploaded">The moments of the sections that were uploaded.</param>
	/// <returns>A task with <see cref="Round.Synced"/>, or <see cref="Round.Raced"/> when the user changed something during the upload.</returns>
	private async Task<Round> MarkSavedAsync(long? revision, SectionTimes uploaded)
	{
		var marked = await local.MarkSavedAsync(revision, uploaded, CurrentOwnerHash).ConfigureAwait(false);
		if (marked.IsFailure)
		{
			return Failed(marked.Error);
		}

		return marked.Value ? Round.Synced : Round.Raced;
	}

	/// <summary>
	/// Reloads the screens; the data is written either way, so a failure is logged and does not fail the sync.
	/// </summary>
	/// <returns>A task that completes when the screens were asked.</returns>
	private async Task ReloadScreensAsync()
	{
		try
		{
			await screens.ReloadAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogReloadFailed(logger, exception.GetType().Name);
		}
	}

	/// <summary>
	/// Classifies a failure: no network is waiting, anything else is an error.
	/// </summary>
	/// <param name="error">The error.</param>
	/// <returns>The round outcome.</returns>
	private Round Failed(Error error)
	{
		LogStopped(logger, error.Code);
		return string.Equals(error.Code, AccountErrors.NetworkCode, StringComparison.Ordinal) ? Round.Offline : Round.Failed;
	}

	/// <summary>
	/// Sets the state and tells the listeners when it changed; the swap is atomic, so two threads never both see the same change.
	/// </summary>
	/// <param name="newState">The new state.</param>
	private void SetState(ProfileSyncState newState)
	{
		if (Interlocked.Exchange(ref state, (int)newState) != (int)newState)
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : How one round of the sync ended.
	/// </summary>
	private enum Round
	{
		/// <summary>
		/// The phone and the server agree.
		/// </summary>
		Synced = 0,

		/// <summary>
		/// The server holds a newer revision than the write was based on.
		/// </summary>
		Stale = 1,

		/// <summary>
		/// The user changed something while the round ran.
		/// </summary>
		Raced = 2,

		/// <summary>
		/// The server could not be reached.
		/// </summary>
		Offline = 3,

		/// <summary>
		/// The round stopped for a reason that another round would not change.
		/// </summary>
		Failed = 4,
	}
	#endregion
}
