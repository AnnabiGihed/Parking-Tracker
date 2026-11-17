using Microsoft.AspNetCore.Components;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

namespace ParkingTracker.Shared.Features.Shared.Layout;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The shell of the app: the page, which fills the screen, and the toast that every feature can use. The page is shown
///              only after the copy of the saved data of the previous version finished (ADR-0008), so that no screen reads the card
///              or the car before it was copied. The wait is bounded: the copy ends, successfully or not, within its own time limit
///              (<see cref="LegacyDataMigration.RunTimeout"/>) and never throws, so the page always opens.
/// </summary>
public sealed partial class MainLayout : LayoutComponentBase
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the migration of the saved data of the previous version.
	/// </summary>
	[Inject]
	public LegacyDataMigration Migration { get; set; } = null!;
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
		await Migration.RunOnceAsync();
		ready = true;
	}
	#endregion
}
