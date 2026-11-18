#if TEST_HOOKS
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ParkingTracker.ViewModels.Features.Shared.Settings.Database;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;
using ParkingTracker.ViewModels.Features.Shared.TestHooks;

namespace ParkingTracker.App.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A component without markup that exists only in a build made with <c>-p:EnableTestHooks=true</c>: it defines
///              <c>window.ptTest</c> in the web view and connects it to a <see cref="TestHookBridge"/>, so the emulator test can drive
///              the app. It is its own root component (<c>#test-hooks</c>) and the csproj leaves this file out of every other build.
/// </summary>
public sealed class TestHookInstaller : ComponentBase, IDisposable
{
	#region Fields
	/// <summary>
	/// The reference that lets JavaScript call the bridge; disposed with the component.
	/// </summary>
	private DotNetObjectReference<TestHookBridge>? bridgeReference;
	#endregion

	#region Injected Services
	/// <summary>
	/// Gets or sets the JavaScript runtime of the web view.
	/// </summary>
	[Inject]
	public IJSRuntime JS { get; set; } = null!;

	/// <summary>
	/// Gets or sets the hooks the emulator test calls.
	/// </summary>
	[Inject]
	public TestHookService Hooks { get; set; } = null!;

	/// <summary>
	/// Gets or sets the migration of the saved data, whose marker the upgrade test reads.
	/// </summary>
	[Inject]
	public LegacyDataMigration Migration { get; set; } = null!;

	/// <summary>
	/// Gets or sets the move of the card, car and timer into the device database, whose marker the upgrade test reads.
	/// </summary>
	[Inject]
	public SettingsToDatabaseMigration DatabaseMigration { get; set; } = null!;
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		bridgeReference?.Dispose();
	}
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override Task OnAfterRenderAsync(bool firstRender)
	{
		return firstRender ? RegisterTestHooksAsync() : Task.CompletedTask;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Defines <c>window.ptTest</c> and hands it the bridge.
	/// </summary>
	/// <returns>A task that completes when the hooks are installed.</returns>
	private async Task RegisterTestHooksAsync()
	{
		await JS.InvokeVoidAsync("eval", TestHookScript.Build()).ConfigureAwait(true);
		bridgeReference = DotNetObjectReference.Create(new TestHookBridge(Hooks, Migration, DatabaseMigration));
		await JS.InvokeVoidAsync(TestHookScript.InstallFunction, bridgeReference).ConfigureAwait(true);
	}
	#endregion
}
#endif
