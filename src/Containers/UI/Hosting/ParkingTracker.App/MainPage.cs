using Microsoft.AspNetCore.Components.WebView.Maui;
using ParkingTracker.App.Features.Shared;
#if TEST_HOOKS
using ParkingTracker.App.Features.Shared.TestHooks;
#endif
using ParkingTracker.Shared.Features.Shared.Layout;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The only page of the app: a Blazor web view that shows <c>wwwroot/index.html</c>, the router of the Shared screens in
///              <c>#app</c>, the platform relay in <c>#platform-relay</c> and, in a test-hooks build, the test hook installer in
///              <c>#test-hooks</c>. It is built in code, without XAML, because it has nothing
///              else to hold. The back button of the phone closes the side menu or the open dialog first and leaves the app only when
///              nothing is open (ADR-0030).
/// </summary>
public sealed class MainPage : ContentPage
{
	#region Constants
	/// <summary>
	/// The colour behind the status bar: the app's dark ink, so the light status icons read in the light and the dark scheme. It matches <c>--fg</c> in <c>Shared/wwwroot/css/style.css</c>.
	/// </summary>
	private const string StatusBarBackground = "#0F172A";
	#endregion

	#region Fields
	/// <summary>
	/// The web view that runs the Blazor screens; the back button is dispatched into it.
	/// </summary>
	private readonly BlazorWebView webView;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MainPage"/> class.
	/// </summary>
	public MainPage()
	{
		// The app is drawn edge to edge, and a web view older than Chromium 136 reports no safe-area inset to the page, so the search
		// bar slid under the status bar; keeping the top edge out of the system bar works on every web view version.
		SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.None, SafeAreaRegions.Container, SafeAreaRegions.None, SafeAreaRegions.None);
		BackgroundColor = Color.FromArgb(StatusBarBackground);
		webView = new BlazorWebView { HostPage = "wwwroot/index.html" };
		webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
		webView.RootComponents.Add(new RootComponent { Selector = "#platform-relay", ComponentType = typeof(PlatformRelay) });
#if TEST_HOOKS
		webView.RootComponents.Add(new RootComponent { Selector = "#test-hooks", ComponentType = typeof(TestHookInstaller) });
#endif
		Content = webView;
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	protected override bool OnBackButtonPressed()
	{
		var backNavigation = Handler?.MauiContext?.Services.GetService<IBackNavigation>();
		if (backNavigation is null || !backNavigation.CanGoBack)
		{
			return base.OnBackButtonPressed();
		}

		// What is open lives in the Blazor scope, so it is closed from there; true tells MAUI the press was handled.
		_ = webView.TryDispatchAsync(_ => backNavigation.TryGoBack());
		return true;
	}
	#endregion
}
