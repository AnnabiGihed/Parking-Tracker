using Microsoft.AspNetCore.Components;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

namespace ParkingTracker.Shared.Features.Shared.Layout;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The shell of the app: the page, which fills the screen, and the toast that every feature can use. The page is shown
///              only after the start-up gate finished (ADR-0008, ADR-0026): the copy of the saved data of the previous version, the
///              schema of the device database and the move of the card, car and timer into it, so that no screen reads the card
///              or the car before they were copied. The wait is bounded: every step ends, successfully or not, within its own time
///              limit (<see cref="LegacyDataMigration.RunTimeout"/>) and the gate never throws, so the page always opens.
/// </summary>
public sealed partial class MainLayout : LayoutComponentBase
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the start-up gate: the migration of the saved data of the previous version and the device database.
	/// </summary>
	[Inject]
	public StartupGate Gate { get; set; } = null!;
	#endregion

	#region Fields
	/// <summary>
	/// Whether the migration finished, so that the page may read the settings.
	/// </summary>
	private bool ready;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override async Task OnInitializedAsync()
	{
		await Gate.RunOnceAsync();
		ready = true;
	}
	#endregion
}
