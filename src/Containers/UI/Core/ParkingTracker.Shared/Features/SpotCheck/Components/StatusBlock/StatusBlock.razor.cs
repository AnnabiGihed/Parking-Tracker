using Microsoft.AspNetCore.Components;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.Shared.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The status block of the bottom sheet: the headline in <c>.hero-title</c> and the explanation in <c>.hero-sub</c>
///              (the classes the emulator test reads), the tariff, the notes, and the actions "I parked here" and the payment
///              applications. The texts are plain and escaped by Razor.
/// </summary>
public sealed partial class StatusBlock : ViewModelComponent<SpotCheckViewModel>
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the car dialog, which "I parked here" opens.
	/// </summary>
	[Inject]
	public CarViewModel Car { get; set; } = null!;

	/// <summary>
	/// Gets or sets the launcher that opens the payment applications.
	/// </summary>
	[Inject]
	public ILinkLauncher Launcher { get; set; } = null!;
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles "I parked here": saves the car and opens its dialog.
	/// </summary>
	private void OnParkedHere() => Car.ParkHere();

	/// <summary>
	/// Handles a payment application button.
	/// </summary>
	/// <param name="app">The application to open.</param>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	private async Task OnPayAppAsync(PayApp app)
	{
		await Launcher.OpenAsync(app.Address);
	}
	#endregion
}
