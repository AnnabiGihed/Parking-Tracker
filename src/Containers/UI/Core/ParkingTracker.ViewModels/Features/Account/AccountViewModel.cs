using System.Globalization;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.Account;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The debug-only account sheet (stage S5; the mandatory sign-in screen is stage S9): signed out it offers "Sign in with Google"
///              and shows why a sign-in failed in plain words; signed in it shows the account from the API (short id, created on, last seen
///              on, active sessions) with Sign out and Delete account, the latter after a confirm step. The sheet is offered only when the
///              build can sign in with Google (<see cref="IsAvailable"/>), which is the case in debug builds. A session the API ended is shown
///              as signed out; a network error is shown and the session is kept. The sheet is drawn by a <c>DialogBridge</c> through
///              <see cref="IDialogViewModel"/>, so this class never references Radzen.
/// </summary>
public sealed class AccountViewModel : ViewModelBase, IDialogViewModel
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
	/// The number of characters of the account id that are shown.
	/// </summary>
	public const int ShortIdLength = 8;
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
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccountViewModel"/> class.
	/// </summary>
	/// <param name="client">The account on the API.</param>
	/// <param name="google">Tells whether this build can sign in with Google.</param>
	/// <param name="toast">Shows the message after a deletion.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public AccountViewModel(ISessionClient client, IGoogleSignIn google, ToastViewModel toast)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(google);
		ArgumentNullException.ThrowIfNull(toast);
		this.client = client;
		this.google = google;
		this.toast = toast;
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
	public bool IsBusy { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the phone holds a session.
	/// </summary>
	public bool IsSignedIn { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the sheet waits for the user to confirm the deletion of the account.
	/// </summary>
	public bool IsConfirmingDelete { get; private set; }

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
		IsOpen = true;
		await RunAsync(async token =>
		{
			IsSignedIn = await client.IsSignedInAsync(token).ConfigureAwait(false);
			if (IsSignedIn)
			{
				await LoadAccountAsync(token).ConfigureAwait(false);
			}
		}, cancellationToken).ConfigureAwait(false);
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
			await LoadAccountAsync(token).ConfigureAwait(false);
		}, cancellationToken);
	}

	/// <summary>
	/// Signs this device out; the sheet shows the signed-out state.
	/// </summary>
	/// <returns>A task that completes when the phone is signed out.</returns>
	public Task SignOutAsync() => SignOutAsync(CancellationToken.None);

	/// <summary>Signs this device out with caller cancellation.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the action finishes.</returns>
	public Task SignOutAsync(CancellationToken cancellationToken)
	{
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

	/// <summary>
	/// Asks the user to confirm the deletion of the account.
	/// </summary>
	public void RequestDelete()
	{
		if (!IsSignedIn || IsBusy)
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
		}, cancellationToken);
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
		if (IsBusy)
		{
			return;
		}

		IsBusy = true;
		Error = string.Empty;
		NotifyChanged();
		try
		{
			await action(cancellationToken).ConfigureAwait(false);
		}
		catch (IOException)
		{
			Error = "Your sign-in details could not be saved or removed securely. Restart the app and try again.";
		}
		finally
		{
			IsBusy = false;
			NotifyChanged();
		}
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
		IsConfirmingDelete = false;
		Account = null;
	}

	/// <summary>
	/// Formats a day for the sheet.
	/// </summary>
	/// <param name="day">The day.</param>
	/// <returns>The day as "17 Nov 2026".</returns>
	private static string Format(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
	#endregion
}
