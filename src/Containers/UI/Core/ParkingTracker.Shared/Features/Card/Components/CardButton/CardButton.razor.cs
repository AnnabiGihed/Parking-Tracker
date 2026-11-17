using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.Shared.Features.Card.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The round card button in the search bar, with a dot that is green when the card is valid, red when it is not or
///              the City list changed, and orange when there is no card. It opens the card dialog.
/// </summary>
public sealed partial class CardButton : ViewModelComponent<CardViewModel>
{
	#region Properties
	/// <summary>
	/// Gets the CSS class of the dot.
	/// </summary>
	private string DotClass => ViewModel.Line.Dot switch
	{
		CardDot.Ok => string.Empty,
		CardDot.Bad => "bad",
		_ => "none",
	};
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles a tap on the button.
	/// </summary>
	/// <returns>A task that completes when the dialog opened.</returns>
	private Task OnClickAsync() => ViewModel.OpenAsync();
	#endregion
}
