using Android.App;
using Android.Content.PM;

// The namespace is the root one, not one per folder: a namespace ParkingTracker.App.Platforms.Android would hide Android.* inside the host.
namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The launcher activity. Its Java name is fixed to <c>be.parkingtracker.app.MainActivity</c>, as in the first version of
///              the app; otherwise .NET generates a <c>crc64…</c> name, shortcuts to the old component break and the emulator test's
///              <c>am start</c> fails. The screen survives rotation and similar changes without restarting the activity.
/// </summary>
[Activity(Name = "be.parkingtracker.app.MainActivity", Theme = "@style/Maui.MainTheme.NoActionBar", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask, ConfigurationChanges = HandledChanges)]
public class MainActivity : MauiAppCompatActivity
{
	#region Constants
	/// <summary>
	/// The configuration changes the activity handles itself, so rotation or a keyboard does not restart the web view.
	/// </summary>
	private const ConfigChanges HandledChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
		| ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.KeyboardHidden
		| ConfigChanges.Keyboard | ConfigChanges.Navigation;
	#endregion
}
