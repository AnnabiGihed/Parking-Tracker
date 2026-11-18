using Microsoft.AspNetCore.Components;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.MapLayers;

namespace ParkingTracker.Shared.Features.MapLayers.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The layers popover: five switches (the card's coverage, parking spaces, roadworks and events, car parks, charging
///              points), the legend of the colours and the line that says what the map shows. A switch is saved by the
///              <see cref="MapLayersViewModel"/>.
/// </summary>
public sealed partial class LayersPopover : ViewModelComponent<MapLayersViewModel>
{
	#region Parameters
	/// <summary>
	/// Gets or sets a value indicating whether the popover is open.
	/// </summary>
	[Parameter]
	public bool IsOpen { get; set; }

	/// <summary>
	/// Gets or sets the callback raised when the user closes the popover.
	/// </summary>
	[Parameter]
	public EventCallback OnClose { get; set; }
	#endregion

	#region Properties
	/// <summary>
	/// Gets the classes of the panel: the popover, hidden until it is opened.
	/// </summary>
	private string PanelClass => IsOpen ? "popover" : "popover hidden";
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles a switch.
	/// </summary>
	/// <param name="layer">The name of the layer.</param>
	/// <param name="isOn">Whether the switch is now on.</param>
	/// <returns>A task that completes when the layers were loaded.</returns>
	private Task OnToggleAsync(string layer, bool isOn) => ViewModel.SetLayerAsync(layer, isOn);
	#endregion
}
