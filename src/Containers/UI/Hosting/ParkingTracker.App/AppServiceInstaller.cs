using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices;
using ParkingTracker.App.Features.Shared;
using ParkingTracker.App.Features.SpotDecisions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;
#if TEST_HOOKS
using ParkingTracker.ViewModels.Features.Shared.TestHooks;
#endif

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers what only the phone can provide: the implementations of the device ports of the Application layer (position,
///              permissions, links, connection, saved settings) and the helpers that join phone events to the view models. They are
///              registered by hand, not by convention, because each one is tied to the platform.
/// </summary>
public sealed class AppServiceInstaller : IServiceInstaller
{
	#region IServiceInstaller Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public void Install(IServiceCollection services, IConfiguration configuration, bool includeConventionBasedRegistration = true)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		services.TryAddSingleton(TimeProvider.System);

		// The card goes to the encrypted storage, everything else to the preferences (ADR-0004).
		services.AddSingleton<ISettingsStore>(provider => new SettingsStore(new SecureValueStorage(provider.GetRequiredService<ILogger<SecureValueStorage>>()), new PreferencesValueStorage()));
		services.AddSingleton<ILocationTracker, GeolocationTracker>();
		services.AddSingleton<ILocationPermissions, MauiLocationPermissions>();
		services.AddSingleton<ILinkLauncher, MauiLinkLauncher>();

		// One instance behind two names: the relay needs the change event, the view models the port.
		services.AddSingleton<ConnectivityNetworkStatus>();
		services.AddSingleton<INetworkStatus>(provider => provider.GetRequiredService<ConnectivityNetworkStatus>());
		services.AddSingleton<AppResumeNotifier>();
		services.TryAddSingleton<IVibration>(Vibration.Default);
		services.AddSingleton<CoverageVibrator>();

#if TEST_HOOKS
		// Scoped like the view models it reads, so that the hooks see the same card line the screen shows.
		services.AddScoped<TestHookService>();
#endif
	}
	#endregion
}
