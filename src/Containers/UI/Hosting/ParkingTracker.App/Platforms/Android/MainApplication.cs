using Android.Runtime;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The Android application object: starts the MAUI app through <see cref="MauiProgram"/>. In a build made with
///              <c>-p:EnableTestHooks=true</c> it also turns web view debugging on, before any web view exists, so that the emulator
///              test can find the <c>webview_devtools_remote_&lt;pid&gt;</c> socket. The attribute is written in full because inside
///              this namespace <c>Application</c> would find the sibling namespace <c>ParkingTracker.Application</c>.
/// </summary>
[global::Android.App.Application]
public class MainApplication : MauiApplication
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MainApplication"/> class; Android calls this with the native handle.
	/// </summary>
	/// <param name="handle">The native object.</param>
	/// <param name="ownership">Who owns the native object.</param>
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override void OnCreate()
	{
#if TEST_HOOKS
		Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
#endif
		base.OnCreate();
	}

	/// <inheritdoc />
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
	#endregion
}
