using System.Globalization;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.ViewModels.Features.ProfileSync;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The account sheet (stage S5; the mandatory sign-in screen is stage S9): signed out it offers "Sign in with
///              Google" and shows why a sign-in failed in plain words; signed in it shows the account from the API (short id,
///              created on, last seen on, active sessions) with Sign out and Delete account, the latter after a confirm step. The
///              phone remembers which account its data belongs to (ADR-0032): the first account to sign in is bound, the same
///              account changes nothing, and another account must confirm that the data of the previous one is removed (or is
///              signed out again); the check runs wherever the account becomes known (sign-in, open, refresh at start), and a
///              mismatch found at start opens the sheet; a deleted account takes its data from the phone too, a removal that did
///              not finish (the flag is written once the server confirmed the deletion) runs again at the next open or refresh,
///              and a wipe that failed can be retried from the signed-out sheet. The profile follows the account (ADR-0032): once the
///              owner is verified (sign-in, open, refresh) a sync is requested; signing out first syncs, and only a sync that ended
///              synced signs out and then removes the profile from the phone, if no change landed meanwhile (the flag is read in the transaction
///              of the removal; a change that did land is asked about too: remove it from the phone or keep it); the wait for that sync is
///              bounded, so that an unreachable server does not hold the sheet, and a sync that did not run counts for nothing; otherwise the sheet asks whether to sign out anyway (the
///              unsaved changes are removed) or to stay signed in; an owner that was never verified signs out without removing anything;
///              and a phone with no places, vehicle or card binds the new account without asking. The sheet is offered only when the build can
///              sign in with Google (<see cref="IsAvailable"/>), which every build with a Google Web client ID can. A session the
///              API ended is shown as signed out; a network error is shown and the session is kept. The sheet is drawn by a
///              <c>DialogBridge</c> through <see cref="IDialogViewModel"/>, so this class never references Radzen.
/// </summary>
public sealed class AccountViewModel : ViewModelBase, IDialogViewModel, IDisposable
{
	#region Constants
	/// <summary>
	/// What is shown when the API ended the session.
	/// </summary>
	public const string SessionEndedText = "Your session ended. Sign in again.";

	/// <summary>
	/// The toast after the account was deleted.
	/// </summary>
	public const string DeletedText = "Account deleted.";

	/// <summary>
	/// What is shown when the account was deleted but its data could not be removed from the phone.
	/// </summary>
	public const string WipeFailedText = "Your account was deleted, but its data could not be removed from this phone. Try again, or clear the app data in the phone settings.";

	/// <summary>
	/// What is shown when the data of the other account could not be removed, so that the new account is not bound.
	/// </summary>
	public const string OwnerChangeFailedText = "The data of the other account could not be removed from this phone. Try again.";

	/// <summary>
	/// What is shown when the phone could not tell which account its data belongs to.
	/// </summary>
	public const string OwnerUnknownText = "This phone could not check which account its saved data belongs to. Sign in again.";

	/// <summary>
	/// What is shown when the phone was signed out but its saved data could not be removed.
	/// </summary>
	public const string SignOutWipeFailedText = "You are signed out, but your saved places, vehicle and card could not be removed from this phone. Clear the app data in the phone settings.";

	/// <summary>
	/// What is shown when the phone was signed out but changes were saved on it after the last sync, so that nothing was removed.
	/// </summary>
	public const string LocalRemovalNoteText = "You are signed out. Some of your latest changes were not saved to your account. Remove your places, vehicle and card from this phone, or keep them on this phone?";

	/// <summary>
	/// The number of characters of the account id that are shown.
	/// </summary>
	public const int ShortIdLength = 8;
	#endregion

	#region Static Instances
	/// <summary>
	/// How long a sign-out waits for the sync before it treats the changes as not saved.
	/// </summary>
	public static readonly TimeSpan SignOutSyncTimeout = TimeSpan.FromSeconds(10);
	#endregion

	#region Fields
	/// <summary>
	/// The account on the API.
	/// </summary>
	private readonly ISessionClient client;

	/// <summary>
	/// Tells whether this build can sign in with Google.
	/// </summary>
	private readonly IGoogleSignIn google;

	/// <summary>
	/// Shows the message after a deletion.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// Remembers which account the data on the phone belongs to.
	/// </summary>
	private readonly IDeviceOwnerStore owner;

	/// <summary>
	/// Removes the data of an account from the phone and refreshes the screens.
	/// </summary>
	private readonly LocalDataEraser eraser;

	/// <summary>
	/// Says whether the data on the phone belongs to the signed-in account, for the sync.
	/// </summary>
	private readonly OwnerVerification verification;

	/// <summary>
	/// Syncs the profile with the account.
	/// </summary>
	private readonly IProfileSync sync;

	/// <summary>
	/// Reads whether the phone holds any place, vehicle or card.
	/// </summary>
	private readonly IProfileLocalStore localProfile;

	/// <summary>
	/// The clock of the bounded wait for the sync.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The hash of the account that signed in while the phone held the data of another one; empty when no decision is pending.
	/// </summary>
	private string pendingOwnerHash = string.Empty;

	/// <summary>
	/// The one gate of the actions and of the refresh: an action waits for a running refresh, and a refresh is skipped while anything holds the gate.
	/// </summary>
	private readonly SemaphoreSlim gate = new(1, 1);

	/// <summary>
	/// Whether an action of the user is running or waiting for the gate, so that a second tap on the same button does nothing.
	/// </summary>
	private bool isActionRunning;

	/// <summary>
	/// Whether a refresh of the state holds the gate.
	/// </summary>
	private bool isRefreshing;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccountViewModel"/> class.
	/// </summary>
	/// <param name="client">The account on the API.</param>
	/// <param name="google">Tells whether this build can sign in with Google.</param>
	/// <param name="toast">Shows the message after a deletion.</param>
	/// <param name="owner">Remembers which account the data on the phone belongs to.</param>
	/// <param name="eraser">Removes the data of an account from the phone and refreshes the screens.</param>
	/// <param name="verification">Says whether the data on the phone belongs to the signed-in account.</param>
	/// <param name="sync">Syncs the profile with the account.</param>
	/// <param name="localProfile">Reads whether the phone holds any place, vehicle or card.</param>
	/// <param name="clock">The clock of the bounded wait for the sync at sign-out.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public AccountViewModel(ISessionClient client, IGoogleSignIn google, ToastViewModel toast, IDeviceOwnerStore owner, LocalDataEraser eraser, OwnerVerification verification, IProfileSync sync, IProfileLocalStore localProfile, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(google);
		ArgumentNullException.ThrowIfNull(toast);
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(eraser);
		ArgumentNullException.ThrowIfNull(verification);
		ArgumentNullException.ThrowIfNull(sync);
		ArgumentNullException.ThrowIfNull(localProfile);
		ArgumentNullException.ThrowIfNull(clock);
		this.client = client;
		this.google = google;
		this.toast = toast;
		this.owner = owner;
		this.eraser = eraser;
		this.verification = verification;
		this.sync = sync;
		this.localProfile = localProfile;
		this.clock = clock;
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets a value indicating whether this build can sign in with Google, so that the menu offers the sheet.
	/// </summary>
	public bool IsAvailable => google.IsAvailable;

	/// <summary>
	/// Gets a value indicating whether a call is running, so that a second tap does nothing.
	/// </summary>
	public bool IsBusy => isActionRunning || isRefreshing;

	/// <summary>
	/// Gets a value indicating whether the phone holds a session.
	/// </summary>
	public bool IsSignedIn { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the sheet waits for the user to confirm the deletion of the account.
	/// </summary>
	public bool IsConfirmingDelete { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the sheet waits for the user to decide about the data of another account that the phone holds.
	/// </summary>
	public bool IsConfirmingOwnerChange { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the data on the phone is known to belong to the signed-in account: the bound hash equals the hash
	/// of that account. It is <see langword="false"/> while signed out, before the check ran, while another account's data is still on
	/// the phone and when the owner could not be read. The sync service must not upload anything unless it is <see langword="true"/>.
	/// </summary>
	public bool IsOwnerVerified => verification.IsVerified;

	/// <summary>
	/// Gets a value indicating whether the sheet waits for the user to decide what to do with changes that are not saved to the account yet:
	/// sign out anyway, which removes them from the phone, or stay signed in.
	/// </summary>
	public bool IsConfirmingSignOut { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the phone was signed out while changes that are not saved to the account were on it, so that the signed-out
	/// sheet asks whether to remove them from the phone or keep them there.
	/// </summary>
	public bool IsConfirmingLocalRemoval { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the account was deleted but its data is still on the phone, so that the signed-out sheet offers to remove it.
	/// </summary>
	public bool IsWipePending { get; private set; }

	/// <summary>
	/// Gets the reason the last action failed, in plain words; empty when there is none.
	/// </summary>
	public string Error { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the account the API returned, or <see langword="null"/> while signed out or before it was read.
	/// </summary>
	public MeResponse? Account { get; private set; }

	/// <summary>
	/// Gets the first characters of the account id; empty while there is no account.
	/// </summary>
	public string AccountIdText => Account is null ? string.Empty : Account.AccountId.ToString("N", CultureInfo.InvariantCulture)[..ShortIdLength];

	/// <summary>
	/// Gets the day the account was created, for example "17 Nov 2026"; empty while there is no account.
	/// </summary>
	public string CreatedText => Account is null ? string.Empty : Format(Account.CreatedOn);

	/// <summary>
	/// Gets the day the account was last seen; empty while there is no account.
	/// </summary>
	public string LastSeenText => Account is null ? string.Empty : Format(Account.LastSeenOn);

	/// <summary>
	/// Gets the number of devices signed in to the account; empty while there is no account.
	/// </summary>
	public string ActiveSessionsText => Account is null ? string.Empty : Account.ActiveSessionCount.ToString(CultureInfo.InvariantCulture);
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the sheet and reads the account when the phone holds a session.
	/// </summary>
	/// <returns>A task that completes when the sheet is open and the account was read or the reason it was not was shown.</returns>
	public Task OpenAsync() => OpenAsync(CancellationToken.None);

	/// <summary>Opens the sheet and reads the account with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the account state is shown.</returns>
	public async Task OpenAsync(CancellationToken cancellationToken)
	{
		if (IsOpen)
		{
			return;
		}

		Error = string.Empty;
		IsConfirmingDelete = false;
		IsConfirmingSignOut = false;
		IsConfirmingLocalRemoval = false;
		IsOpen = true;
		await RunAsync(async token =>
		{
			IsSignedIn = await client.IsSignedInAsync(token).ConfigureAwait(false);
			if (IsSignedIn)
			{
				await LoadAccountAsync(token).ConfigureAwait(false);
			}

			await CheckAccountAsync(freshSignIn: false, token).ConfigureAwait(false);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Reads the signed-in state for the menu: whether the phone holds a session (a local check, no network) and, when it does and the
	/// account is not known yet, the account. A failure is not shown: the menu falls back to what it knows, and a session the API ended shows as signed out.
	/// </summary>
	/// <returns>A task that completes when the state was read.</returns>
	public Task RefreshStateAsync() => RefreshStateAsync(CancellationToken.None);

	/// <summary>Reads the signed-in state for the menu with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the state was read.</returns>
	public async Task RefreshStateAsync(CancellationToken cancellationToken)
	{
		// A refresh gives way to everything else: it is skipped while an action runs or waits, and while another refresh runs.
		if (!IsAvailable || isActionRunning || !await gate.WaitAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false))
		{
			return;
		}

		isRefreshing = true;
		NotifyChanged();
		try
		{
			IsSignedIn = await client.IsSignedInAsync(cancellationToken).ConfigureAwait(false);
			if (IsSignedIn && Account is null)
			{
				await LoadAccountQuietlyAsync(cancellationToken).ConfigureAwait(false);
			}

			if (!IsSignedIn)
			{
				Account = null;
			}

			await CheckAccountAsync(freshSignIn: false, cancellationToken).ConfigureAwait(false);
		}
		catch (IOException)
		{
			// The secure storage could not be read: the menu keeps what it knew, and the sheet shows the reason when it is opened.
		}
		finally
		{
			isRefreshing = false;
			_ = gate.Release();
		}

		if (IsConfirmingOwnerChange)
		{
			// The data of another account is on the phone: the question cannot wait until the user opens the sheet.
			IsOpen = true;
		}

		if (!IsOpen)
		{
			Error = string.Empty;
		}

		NotifyChanged();
	}

	/// <summary>
	/// Opens the sheet straight into the confirmation of the deletion of the account.
	/// </summary>
	/// <returns>A task that completes when the sheet is open and, for a signed-in user, asks for the confirmation.</returns>
	public async Task OpenToDeleteAsync()
	{
		await OpenAsync().ConfigureAwait(false);

		// A refresh that was running when the sheet opened may still hold the gate; the prompt must not be lost to it.
		await gate.WaitAsync().ConfigureAwait(false);
		_ = gate.Release();
		RequestDelete();
	}

	/// <inheritdoc />
	public void Close()
	{
		if (!IsOpen)
		{
			return;
		}

		IsOpen = false;
		IsConfirmingDelete = false;
		IsConfirmingSignOut = false;
		IsConfirmingLocalRemoval = false;
		NotifyChanged();
	}

	/// <summary>
	/// Signs in with Google and then reads the account; a failure is shown in words.
	/// </summary>
	/// <returns>A task that completes when the user is signed in or the reason they are not was shown.</returns>
	public Task SignInAsync() => SignInAsync(CancellationToken.None);

	/// <summary>Signs in with Google and reads the account with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task SignInAsync(CancellationToken cancellationToken)
	{
		return RunAsync(async token =>
		{
			var result = await client.SignInAsync(token).ConfigureAwait(false);
			if (result.IsFailure)
			{
				Error = result.Error.Message;
				return;
			}

			IsSignedIn = true;
			IsWipePending = false;
			IsConfirmingLocalRemoval = false;
			await LoadAccountAsync(token).ConfigureAwait(false);
			await CheckAccountAsync(freshSignIn: true, token).ConfigureAwait(false);
		}, cancellationToken);
	}

	/// <summary>
	/// Signs this device out; the sheet shows the signed-out state. When the data on the phone belongs to the account, the profile is
	/// synced first: only a sync that ended synced signs out and removes the profile from the phone, anything else asks first.
	/// </summary>
	/// <returns>A task that completes when the phone is signed out or the question was asked.</returns>
	public Task SignOutAsync() => SignOutAsync(CancellationToken.None);

	/// <summary>Signs this device out with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task SignOutAsync(CancellationToken cancellationToken)
	{
		return RunAsync(async token =>
		{
			if (!IsOwnerVerified)
			{
				// The data on the phone was never synced to this account, so it is not this account's to remove.
				await SignOutOnServerAsync(WipeMode.None, token).ConfigureAwait(false);
				return;
			}

			// Only a sync that ran for this request and ended synced counts; a state kept from an earlier run proves nothing.
			var result = await SyncForSignOutAsync(token).ConfigureAwait(false);
			if (result.IsSaved)
			{
				await SignOutOnServerAsync(WipeMode.WhenSaved, token).ConfigureAwait(false);
				return;
			}

			IsConfirmingSignOut = true;
			IsOpen = true;
		}, cancellationToken);
	}

	/// <summary>
	/// Signs out although changes are not saved to the account yet, and removes the profile from the phone.
	/// </summary>
	/// <returns>A task that completes when the phone is signed out.</returns>
	public Task ConfirmSignOutAsync() => ConfirmSignOutAsync(CancellationToken.None);

	/// <summary>Signs out anyway with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task ConfirmSignOutAsync(CancellationToken cancellationToken)
	{
		if (!IsConfirmingSignOut)
		{
			return Task.CompletedTask;
		}

		return RunAsync(async token =>
		{
			IsConfirmingSignOut = false;
			await SignOutOnServerAsync(WipeMode.Always, token).ConfigureAwait(false);
		}, cancellationToken);
	}

	/// <summary>
	/// Removes the places, vehicle and card from the phone after the sign-out, because the user chose to.
	/// </summary>
	/// <returns>A task that completes when the data was removed or the reason it was not was shown.</returns>
	public Task ConfirmLocalRemovalAsync() => ConfirmLocalRemovalAsync(CancellationToken.None);

	/// <summary>Removes the data from the phone after the sign-out with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task ConfirmLocalRemovalAsync(CancellationToken cancellationToken)
	{
		if (!IsConfirmingLocalRemoval)
		{
			return Task.CompletedTask;
		}

		return RunAsync(async _ =>
		{
			IsConfirmingLocalRemoval = false;
			var wiped = await eraser.WipeProfileAsync(CancellationToken.None).ConfigureAwait(false);
			if (wiped.IsFailure)
			{
				Error = SignOutWipeFailedText;
			}
		}, cancellationToken);
	}

	/// <summary>
	/// Keeps the changes that are not saved on the phone after the sign-out; they are saved to an account when one signs in.
	/// </summary>
	public void KeepOnPhone()
	{
		IsConfirmingLocalRemoval = false;
		NotifyChanged();
	}

	/// <summary>
	/// Withdraws the sign-out: the phone stays signed in and keeps its data.
	/// </summary>
	public void CancelSignOut()
	{
		IsConfirmingSignOut = false;
		NotifyChanged();
	}

	/// <summary>
	/// Asks the user to confirm the deletion of the account.
	/// </summary>
	public void RequestDelete()
	{
		if (!IsSignedIn || isActionRunning)
		{
			return;
		}

		IsConfirmingDelete = true;
		NotifyChanged();
	}

	/// <summary>
	/// Withdraws the request to delete the account.
	/// </summary>
	public void CancelDelete()
	{
		IsConfirmingDelete = false;
		NotifyChanged();
	}

	/// <summary>
	/// Deletes the account on the API; a deleted account shows a toast and the signed-out state, a refused deletion is shown in words and keeps the session.
	/// </summary>
	/// <returns>A task that completes when the account was deleted or the reason it was not was shown.</returns>
	public Task ConfirmDeleteAsync() => ConfirmDeleteAsync(CancellationToken.None);

	/// <summary>Deletes the account after confirmation with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task ConfirmDeleteAsync(CancellationToken cancellationToken)
	{
		if (!IsConfirmingDelete)
		{
			return Task.CompletedTask;
		}

		return RunAsync(async token =>
		{
			IsConfirmingDelete = false;
			var result = await client.DeleteAccountAsync(token).ConfigureAwait(false);
			if (result.IsFailure)
			{
				ShowFailure(result.Error);
				return;
			}

			ShowSignedOut();
			toast.Show(DeletedText);

			// The server confirmed the deletion, so the flag is safe to write and the wipe is safe to repeat; neither stops on the token.
			// A flag that cannot be written does not stop the wipe, which only loses the repeat after a restart.
			_ = await owner.MarkErasePendingAsync(CancellationToken.None).ConfigureAwait(false);
			await WipeAfterDeletionAsync(CancellationToken.None).ConfigureAwait(false);
		}, cancellationToken);
	}

	/// <summary>
	/// Removes the data of the deleted account from the phone again after the first attempt failed.
	/// </summary>
	/// <returns>A task that completes when the data was removed or the reason it was not was shown.</returns>
	public Task RetryWipeAsync() => RetryWipeAsync(CancellationToken.None);

	/// <summary>Removes the data of the deleted account from the phone again with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task RetryWipeAsync(CancellationToken cancellationToken)
	{
		if (!IsWipePending)
		{
			return Task.CompletedTask;
		}

		return RunAsync(WipeAfterDeletionAsync, cancellationToken);
	}

	/// <summary>
	/// Removes the data of the previous account from the phone and binds the account that just signed in.
	/// </summary>
	/// <returns>A task that completes when the data was removed and the account bound, or the reason it was not was shown.</returns>
	public Task ConfirmOwnerChangeAsync() => ConfirmOwnerChangeAsync(CancellationToken.None);

	/// <summary>Removes the data of the previous account and binds the new one with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task ConfirmOwnerChangeAsync(CancellationToken cancellationToken)
	{
		if (!IsConfirmingOwnerChange)
		{
			return Task.CompletedTask;
		}

		return RunAsync(async token =>
		{
			var wiped = await eraser.WipeProfileAsync(token).ConfigureAwait(false);
			if (wiped.IsFailure)
			{
				Error = OwnerChangeFailedText;
				return;
			}

			var bound = await owner.BindAsync(pendingOwnerHash, token).ConfigureAwait(false);
			if (bound.IsFailure)
			{
				Error = OwnerUnknownText;
				return;
			}

			verification.Verify(pendingOwnerHash);
			ClearOwnerChange();
			sync.Request();
		}, cancellationToken);
	}

	/// <summary>
	/// Signs the new account out again and leaves the data of the previous account on the phone.
	/// </summary>
	/// <returns>A task that completes when the phone is signed out.</returns>
	public Task CancelOwnerChangeAsync() => CancelOwnerChangeAsync(CancellationToken.None);

	/// <summary>Signs the new account out again with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task CancelOwnerChangeAsync(CancellationToken cancellationToken)
	{
		if (!IsConfirmingOwnerChange)
		{
			return Task.CompletedTask;
		}

		return RunAsync(async token =>
		{
			var result = await client.SignOutAsync(token).ConfigureAwait(false);
			if (result.IsFailure)
			{
				Error = result.Error.Message;
				return;
			}

			ShowSignedOut();
		}, cancellationToken);
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		gate.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Runs an action while the sheet is busy and draws the result, whatever the outcome.
	/// </summary>
	/// <param name="action">The action.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action ended and the sheet was drawn.</returns>
	private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
	{
		if (isActionRunning)
		{
			return;
		}

		isActionRunning = true;
		Error = string.Empty;
		NotifyChanged();
		var holdsGate = false;
		try
		{
			// A refresh that is running finishes first, so that it cannot overwrite what this action sets. The wait is short and does not
			// take the token, so that the session client still sees it and reports the cancellation.
			await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
			holdsGate = true;
			await action(cancellationToken).ConfigureAwait(false);
		}
		catch (IOException)
		{
			Error = "Your sign-in details could not be saved or removed securely. Restart the app and try again.";
		}
		finally
		{
			if (holdsGate)
			{
				_ = gate.Release();
			}

			isActionRunning = false;
			NotifyChanged();
		}
	}

	/// <summary>
	/// Runs the sync that a sign-out waits for, for at most <see cref="SignOutSyncTimeout"/>. The sync itself goes on after the wait ended.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task with the result of the sync, or a result that says no sync ended in time.</returns>
	private async Task<ProfileSyncResult> SyncForSignOutAsync(CancellationToken cancellationToken)
	{
		using var timeout = new CancellationTokenSource(SignOutSyncTimeout, clock);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
		try
		{
			return await sync.SyncAsync(linked.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			return ProfileSyncResult.NotRun(sync.State);
		}
	}

	/// <summary>
	/// Signs the device out on the server and shows the signed-out state; the owner stays bound to the phone. A failed sign-out removes
	/// nothing, because the phone is still signed in.
	/// </summary>
	/// <param name="mode">Whether and how the places, vehicle and card are removed from the phone afterwards.</param>
	/// <param name="cancellationToken">A token to cancel the sign-out call.</param>
	/// <returns>A task that completes when the phone is signed out and, when asked, emptied.</returns>
	private async Task SignOutOnServerAsync(WipeMode mode, CancellationToken cancellationToken)
	{
		var result = await client.SignOutAsync(cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			Error = result.Error.Message;
			return;
		}

		// The verification goes first, so that no sync starts on data that is about to be removed.
		ShowSignedOut();
		if (mode == WipeMode.None)
		{
			return;
		}

		// The sign-out is done, so the removal does not stop on the token. After a sync the removal is conditional: the changed flag is read in
		// its transaction, and a change that was saved since the sync is asked about, not removed.
		var wiped = mode == WipeMode.Always
			? await eraser.WipeProfileAsync(CancellationToken.None).ConfigureAwait(false)
			: await WipeWhenStillSavedAsync().ConfigureAwait(false);
		if (wiped.IsFailure)
		{
			Error = SignOutWipeFailedText;
		}
	}

	/// <summary>
	/// Removes the profile unless a change landed since the sync; then it asks the user what to do with it.
	/// </summary>
	/// <returns>A task with the result of the removal; a success also when the question was asked instead.</returns>
	private async Task<Result> WipeWhenStillSavedAsync()
	{
		var wiped = await eraser.WipeProfileIfSavedAsync(CancellationToken.None).ConfigureAwait(false);
		if (wiped.IsSuccess && !wiped.Value)
		{
			IsConfirmingLocalRemoval = true;
			IsOpen = true;
		}

		return wiped.IsFailure ? Result.Failure(wiped.Error, wiped.ResultExceptionType) : Result.Success();
	}

	/// <summary>
	/// Reads the account; a session the API ended shows the signed-out state with a reason, any other failure is shown and the session is kept.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the account was read or the reason it was not was recorded.</returns>
	private async Task LoadAccountAsync(CancellationToken cancellationToken)
	{
		var me = await client.GetMeAsync(cancellationToken).ConfigureAwait(false);
		if (me.IsSuccess)
		{
			Account = me.Value;
			return;
		}

		ShowFailure(me.Error);
	}

	/// <summary>
	/// Reads the account without showing a failure; a session the API ended shows as signed out, any other failure leaves the account unknown.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the account was read or the attempt was dropped.</returns>
	private async Task LoadAccountQuietlyAsync(CancellationToken cancellationToken)
	{
		var me = await client.GetMeAsync(cancellationToken).ConfigureAwait(false);
		if (me.IsSuccess)
		{
			Account = me.Value;
		}
		else if (string.Equals(me.Error.Code, AccountErrors.NotSignedInCode, StringComparison.Ordinal))
		{
			ShowSignedOut();
		}
	}

	/// <summary>
	/// Runs wherever the state of the account becomes known: a removal that the server's deletion left unfinished is finished, and a
	/// signed-in account is compared with the one the data of the phone belongs to. The owner row is the source of truth, so nothing is
	/// remembered between calls that the next call could not find out again.
	/// </summary>
	/// <param name="freshSignIn">Whether the account signed in a moment ago, as opposed to being read again.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the check ran.</returns>
	private async Task CheckAccountAsync(bool freshSignIn, CancellationToken cancellationToken)
	{
		// The deletion was confirmed by the server, so a pending removal is finished whatever the sign-in state is, before any owner check.
		await ResumePendingEraseAsync(cancellationToken).ConfigureAwait(false);
		if (!IsSignedIn)
		{
			verification.Clear();
			return;
		}

		if (Account is not null)
		{
			await VerifyOwnerAsync(Account, freshSignIn, cancellationToken).ConfigureAwait(false);
		}

		if (IsOwnerVerified)
		{
			sync.Request();
		}
	}

	/// <summary>
	/// Compares the signed-in account with the one the data of the phone belongs to: the first account is bound, the same account
	/// changes nothing, and another account waits for the decision of the user. When the owner cannot be read right after a sign-in,
	/// the new account is signed out again, because it must not stay signed in unbound; later the check is only tried again.
	/// </summary>
	/// <param name="account">The signed-in account.</param>
	/// <param name="freshSignIn">Whether the account signed in a moment ago.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the account was bound or accepted, or the decision was asked for.</returns>
	private async Task VerifyOwnerAsync(MeResponse account, bool freshSignIn, CancellationToken cancellationToken)
	{
		var hash = DeviceOwnerHash.Of(account.AccountId);
		var bound = await owner.GetAccountHashAsync(cancellationToken).ConfigureAwait(false);
		if (bound.IsSuccess)
		{
			await VerifyBoundOwnerAsync(hash, string.Equals(bound.Value, hash, StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
			return;
		}

		verification.Clear();
		if (DeviceDataErrors.IsNotStored(bound))
		{
			// The data already on the phone belongs to the first account that signs in.
			var result = await owner.BindAsync(hash, cancellationToken).ConfigureAwait(false);
			if (result.IsSuccess)
			{
				verification.Verify(hash);
				return;
			}
		}

		if (freshSignIn)
		{
			await RejectUnboundSignInAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Handles an account whose hash is compared with the bound one: the same account is verified, another account takes over a phone
	/// that holds nothing worth protecting, and otherwise waits for the decision of the user.
	/// </summary>
	/// <param name="hash">The hash of the signed-in account.</param>
	/// <param name="same">Whether it equals the bound hash.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the account was verified or the decision was asked for.</returns>
	private async Task VerifyBoundOwnerAsync(string hash, bool same, CancellationToken cancellationToken)
	{
		if (same)
		{
			verification.Verify(hash);
			IsConfirmingOwnerChange = false;
			pendingOwnerHash = string.Empty;
			return;
		}

		verification.Clear();
		if (await IsLocalProfileEmptyAsync(cancellationToken).ConfigureAwait(false) && await TakeOverEmptyPhoneAsync(hash, cancellationToken).ConfigureAwait(false))
		{
			return;
		}

		IsConfirmingOwnerChange = true;
		pendingOwnerHash = hash;
	}

	/// <summary>
	/// Tells whether the phone holds no place, vehicle or card. A profile that cannot be read counts as not empty, so nothing is removed unasked.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task with <see langword="true"/> when nothing is saved.</returns>
	private async Task<bool> IsLocalProfileEmptyAsync(CancellationToken cancellationToken)
	{
		var read = await localProfile.ReadAsync(cancellationToken).ConfigureAwait(false);
		return read.IsSuccess && read.Value.IsEmpty;
	}

	/// <summary>
	/// Binds an account to a phone that holds nothing to protect. The sync state of the previous account goes first: its base revision
	/// must not be matched against the revisions of this account.
	/// </summary>
	/// <param name="hash">The hash of the signed-in account.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task with <see langword="true"/> when the account is bound and verified.</returns>
	private async Task<bool> TakeOverEmptyPhoneAsync(string hash, CancellationToken cancellationToken)
	{
		var wiped = await eraser.WipeProfileAsync(cancellationToken).ConfigureAwait(false);
		if (wiped.IsFailure)
		{
			return false;
		}

		var bound = await owner.BindAsync(hash, cancellationToken).ConfigureAwait(false);
		if (bound.IsFailure)
		{
			return false;
		}

		verification.Verify(hash);
		return true;
	}

	/// <summary>
	/// Signs the account out again after a sign-in whose owner check failed, and says why.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the sign-out ended.</returns>
	private async Task RejectUnboundSignInAsync(CancellationToken cancellationToken)
	{
		var result = await client.SignOutAsync(cancellationToken).ConfigureAwait(false);
		if (result.IsSuccess)
		{
			ShowSignedOut();
		}

		Error = OwnerUnknownText;
	}

	/// <summary>
	/// Finishes the removal of the data of a deleted account that an earlier run of the app did not finish.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the flag was read and, when it was set, the wipe ended.</returns>
	private async Task ResumePendingEraseAsync(CancellationToken cancellationToken)
	{
		var pending = await owner.GetErasePendingAsync(cancellationToken).ConfigureAwait(false);
		if (pending.IsSuccess && pending.Value)
		{
			await WipeAfterDeletionAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Removes everything of the deleted account from the phone; a failure is shown and can be retried.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the wipe ended.</returns>
	private async Task WipeAfterDeletionAsync(CancellationToken cancellationToken)
	{
		var wiped = await eraser.WipeAllAsync(cancellationToken).ConfigureAwait(false);
		IsWipePending = wiped.IsFailure;
		if (wiped.IsFailure)
		{
			Error = WipeFailedText;
		}
	}

	/// <summary>
	/// Forgets the decision about the data of another account.
	/// </summary>
	private void ClearOwnerChange()
	{
		IsConfirmingOwnerChange = false;
		pendingOwnerHash = string.Empty;
	}

	/// <summary>
	/// Records a failure of a call as the signed-in user.
	/// </summary>
	/// <param name="error">The failure.</param>
	private void ShowFailure(Pivot.Framework.Domain.Shared.Error error)
	{
		if (string.Equals(error.Code, AccountErrors.NotSignedInCode, StringComparison.Ordinal))
		{
			ShowSignedOut();
			Error = SessionEndedText;
			return;
		}

		Error = error.Message;
	}

	/// <summary>
	/// Puts the sheet in the signed-out state.
	/// </summary>
	private void ShowSignedOut()
	{
		IsSignedIn = false;
		verification.Clear();
		sync.Reset();
		IsConfirmingDelete = false;
		IsConfirmingSignOut = false;
		ClearOwnerChange();
		Account = null;
	}

	/// <summary>
	/// Formats a day for the sheet.
	/// </summary>
	/// <param name="day">The day.</param>
	/// <returns>The day as "17 Nov 2026".</returns>
	private static string Format(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Whether the places, vehicle and card are removed from the phone after a sign-out.
	/// </summary>
	private enum WipeMode
	{
		/// <summary>
		/// Nothing is removed: the data was never synced to this account.
		/// </summary>
		None = 0,

		/// <summary>
		/// The data is removed unless a change landed since the sync.
		/// </summary>
		WhenSaved = 1,

		/// <summary>
		/// The data is removed because the user asked for it.
		/// </summary>
		Always = 2,
	}
	#endregion
}
