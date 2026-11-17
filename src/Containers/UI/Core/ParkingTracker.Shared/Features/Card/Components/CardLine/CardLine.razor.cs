using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.Shared.Features.Card.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The card line of the bottom sheet: where the card is valid, until when (a warning in the last 30 days with a link
///              that e-mails the City to renew), or "Add my card" when there is none.
/// </summary>
public sealed partial class CardLine : ViewModelComponent<CardViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles "Add my card".
	/// </summary>
	/// <returns>A task that completes when the dialog opened.</returns>
	private Task OnAddAsync() => ViewModel.OpenAsync();

	/// <summary>
	/// Handles "renew".
	/// </summary>
	/// <returns>A task that completes when the e-mail link was handed to the launcher.</returns>
	private Task OnRenewAsync() => ViewModel.OpenRenewalAsync();
	#endregion
}
