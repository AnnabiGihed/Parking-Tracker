using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Account;

namespace ParkingTracker.Shared.Features.Account.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The account sheet (stage S5), shown as a bottom sheet through the <c>DialogBridge</c>: "Sign in with Google"
///              while signed out, and the account from the API with Sign out and Delete account (after a confirm step) while
///              signed in. After a sign-in with another account than the one the phone's data belongs to it asks whether to
///              remove that data, and after a deletion whose wipe failed it offers to try again. It hands every tap to the
///              <see cref="AccountViewModel"/>, which holds the state and the words of the failures.
/// </summary>
public sealed partial class AccountSheet : ViewModelComponent<AccountViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles "Sign in with Google".
	/// </summary>
	/// <returns>A task that completes when the sign-in ended.</returns>
	private Task OnSignInAsync() => ViewModel.SignInAsync();

	/// <summary>
	/// Handles "Sign out".
	/// </summary>
	/// <returns>A task that completes when the phone is signed out.</returns>
	private Task OnSignOutAsync() => ViewModel.SignOutAsync();

	/// <summary>
	/// Handles "Delete account", which asks for a confirmation.
	/// </summary>
	private void OnDelete() => ViewModel.RequestDelete();

	/// <summary>
	/// Handles "Keep account".
	/// </summary>
	private void OnCancelDelete() => ViewModel.CancelDelete();

	/// <summary>
	/// Handles the confirmation of the deletion.
	/// </summary>
	/// <returns>A task that completes when the account was deleted or the reason it was not was shown.</returns>
	private Task OnConfirmDeleteAsync() => ViewModel.ConfirmDeleteAsync();

	/// <summary>
	/// Handles "Remove data from this phone", which tries again to remove the data of a deleted account.
	/// </summary>
	/// <returns>A task that completes when the data was removed or the reason it was not was shown.</returns>
	private Task OnRetryWipeAsync() => ViewModel.RetryWipeAsync();

	/// <summary>
	/// Handles "Remove and continue" for the data of another account.
	/// </summary>
	/// <returns>A task that completes when the data was removed or the reason it was not was shown.</returns>
	private Task OnConfirmOwnerChangeAsync() => ViewModel.ConfirmOwnerChangeAsync();

	/// <summary>
	/// Handles "Cancel" for the data of another account, which signs the new account out.
	/// </summary>
	/// <returns>A task that completes when the phone is signed out.</returns>
	private Task OnCancelOwnerChangeAsync() => ViewModel.CancelOwnerChangeAsync();
	#endregion
}
