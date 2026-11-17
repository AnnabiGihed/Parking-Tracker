using Microsoft.AspNetCore.Components.WebView.Maui;
using ParkingTracker.App.Features.Shared;
#if TEST_HOOKS
using ParkingTracker.App.Features.Shared.TestHooks;
#endif
using ParkingTracker.Shared.Features.Shared.Layout;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The only page of the app: a Blazor web view that shows <c>wwwroot/index.html</c>, the router of the Shared screens in
///              <c>#app</c>, the platform relay in <c>#platform-relay</c> and, in a test-hooks build, the test hook installer in
///              <c>#test-hooks</c>. It is built in code, without XAML, because it has nothing
///              else to hold.
/// </summary>
public sealed class MainPage : ContentPage
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MainPage"/> class.
	/// </summary>
	public MainPage()
	{
		var webView = new BlazorWebView { HostPage = "wwwroot/index.html" };
		webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
		webView.RootComponents.Add(new RootComponent { Selector = "#platform-relay", ComponentType = typeof(PlatformRelay) });
#if TEST_HOOKS
		webView.RootComponents.Add(new RootComponent { Selector = "#test-hooks", ComponentType = typeof(TestHookInstaller) });
#endif
		Content = webView;
	}
	#endregion
}
