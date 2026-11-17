using Microsoft.AspNetCore.Components.WebView.Maui;
using ParkingTracker.App.Features.Shared;
using ParkingTracker.Shared.Features.Shared.Layout;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The only page of the app: a Blazor web view that shows <c>wwwroot/index.html</c>, the router of the Shared screens in
///              <c>#app</c> and the platform relay in <c>#platform-relay</c>. It is built in code, without XAML, because it has nothing
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
		Content = webView;
	}
	#endregion
}
