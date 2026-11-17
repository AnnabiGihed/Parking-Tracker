using Microsoft.Extensions.Logging;
using ParkingTracker.Application;
using ParkingTracker.Infrastructure;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;
using ScreenServiceInstaller = ParkingTracker.Shared.UiServiceInstaller;
using ViewModelServiceInstaller = ParkingTracker.ViewModels.UiServiceInstaller;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The composition root of the app: creates the MAUI app and lets each layer register its own services through its
///              installer. It holds no registrations of its own, so that what each layer provides stays next to that layer.
/// </summary>
public static class MauiProgram
{
	#region Public Methods
	/// <summary>
	/// Builds the MAUI app: the Blazor web view, logging to logcat and the services of every layer.
	/// </summary>
	/// <returns>The app, which the Android application object starts.</returns>
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();
		builder.Services.AddMauiBlazorWebView();
		builder.Logging.AddDebug();

		IServiceInstaller[] installers =
		[
			new ApplicationServiceInstaller(),
			new InfrastructureServiceInstaller(),
			new ViewModelServiceInstaller(),
			new ScreenServiceInstaller(),
			new AppServiceInstaller(),
		];
		foreach (var installer in installers)
		{
			installer.Install(builder.Services, builder.Configuration);
		}

		return builder.Build();
	}
	#endregion
}
