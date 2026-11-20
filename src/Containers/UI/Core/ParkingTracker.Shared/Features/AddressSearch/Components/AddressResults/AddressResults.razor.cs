using Microsoft.AspNetCore.Components;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Features.AddressSearch.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The list of matches of an address search, on a Radzen card: one button per match with a pin, the label in bold and
///              the locality below it. It is shared by the screens that search an address, so it knows no view model: the owner gives it
///              the matches and hears of a pick or a close. It exists only while the list is open, and while it exists the back button of
///              the phone closes it.
/// </summary>
public sealed partial class AddressResults : ComponentBase, IDisposable
{
	#region Constants
	/// <summary>
	/// The text shown for a match whose service gave neither a label nor a name.
	/// </summary>
	private const string UnnamedLabel = "Unnamed place";
	#endregion

	#region Fields
	/// <summary>
	/// Makes the back button close the list while it is open.
	/// </summary>
	private IDisposable? backRegistration;
	#endregion

	#region Parameters
	/// <summary>
	/// Gets or sets the matches to choose from, best first.
	/// </summary>
	[Parameter]
	[EditorRequired]
	public IReadOnlyList<AddressMatch> Matches { get; set; } = [];

	/// <summary>
	/// Gets or sets the callback that receives the match the user picked.
	/// </summary>
	[Parameter]
	public EventCallback<AddressMatch> OnPick { get; set; }

	/// <summary>
	/// Gets or sets the callback that is called when the back button asks to close the list.
	/// </summary>
	[Parameter]
	public EventCallback OnClose { get; set; }
	#endregion

	#region Injected Services
	/// <summary>
	/// Gets or sets the stack of things the back button closes.
	/// </summary>
	[Inject]
	public IBackNavigation BackNavigation { get; set; } = null!;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnInitialized()
	{
		// The back button is pressed outside the Blazor dispatcher, so the callback goes through it.
		backRegistration = BackNavigation.Register(() => _ = InvokeAsync(OnClose.InvokeAsync));
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		backRegistration?.Dispose();
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles a tap on a match.
	/// </summary>
	/// <param name="match">The match that was tapped.</param>
	/// <returns>A task that completes when the owner handled the pick.</returns>
	private Task OnPickAsync(AddressMatch match) => OnPick.InvokeAsync(match);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gets the first line of a match.
	/// </summary>
	/// <param name="match">The match.</param>
	/// <returns>The label, else the full name, else a neutral text.</returns>
	private static string LabelOf(AddressMatch match) => match.Label ?? match.DisplayName ?? UnnamedLabel;
	#endregion
}
