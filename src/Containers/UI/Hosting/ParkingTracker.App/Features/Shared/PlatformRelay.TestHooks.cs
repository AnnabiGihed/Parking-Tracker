#if TEST_HOOKS
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ParkingTracker.App.Features.Shared.TestHooks;
using ParkingTracker.ViewModels.Features.Shared.TestHooks;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The part of <see cref="PlatformRelay"/> that exists only in a build made with <c>-p:EnableTestHooks=true</c>: it defines
///              <c>window.ptTest</c> in the web view and connects it to a <see cref="TestHookBridge"/>. The csproj leaves this file out
///              of every other build.
/// </summary>
public sealed partial class PlatformRelay
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
	#endregion

	#region Private Helpers
	/// <summary>
	/// Defines <c>window.ptTest</c> and hands it the bridge.
	/// </summary>
	/// <returns>A task that completes when the hooks are installed.</returns>
	private async Task RegisterTestHooksAsync()
	{
		await JS.InvokeVoidAsync("eval", TestHookScript.Build()).ConfigureAwait(true);
		bridgeReference = DotNetObjectReference.Create(new TestHookBridge(Hooks));
		await JS.InvokeVoidAsync(TestHookScript.InstallFunction, bridgeReference).ConfigureAwait(true);
	}

	/// <summary>
	/// Releases the bridge reference.
	/// </summary>
	private void DisposeTestHooks()
	{
		bridgeReference?.Dispose();
	}
	#endregion
}
#endif
