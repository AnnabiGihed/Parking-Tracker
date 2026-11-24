using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using ParkingTracker.App.Features.Accounts;
using ParkingTracker.App.Features.Cards;
using ParkingTracker.App.Features.ParkingDetection;
using ParkingTracker.App.Features.Shared;
using ParkingTracker.App.Features.SpotDecisions;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.Cards.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Infrastructure.Features.Accounts;
using ParkingTracker.Infrastructure.Features.Shared.Legacy;
using ParkingTracker.Persistence.Device;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Shared.Settings.Database;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;
#if TEST_HOOKS
using ParkingTracker.ViewModels.Features.Shared.TestHooks;
#endif

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers what only the phone can provide: the implementations of the device ports of the Application layer
///              (position, permissions, links, card photo text, connection, saved settings, notifications, alarms, the background service of the
///              parking alerts, whether the app is on screen, Google sign-in and the stored session tokens) and the helpers that join phone events to the view models. They are
///              registered by hand, not by convention, because each one is tied to the platform.
/// </summary>
public sealed class AppServiceInstaller : IServiceInstaller
{
	#region Constants
	/// <summary>
	/// The name of the database file in <see cref="FileSystem.AppDataDirectory"/>; the backup rules name the same file.
	/// </summary>
	private const string DatabaseFileName = "parkingtracker.db";
	#endregion

	#region IServiceInstaller Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public void Install(IServiceCollection services, IConfiguration configuration, bool includeConventionBasedRegistration = true)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		services.TryAddSingleton(TimeProvider.System);
		AddStorage(services);
		AddStartupGate(services);
		AddDeviceServices(services);
		AddAccountServices(services);

#if TEST_HOOKS
		// Scoped like the view models it reads, so that the hooks see the same card line the screen shows.
		services.AddScoped<TestHookService>();
#endif
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Registers the device database, the key-value store of the settings and the settings store the screens read.
	/// </summary>
	/// <param name="services">The service collection.</param>
	private static void AddStorage(IServiceCollection services)
	{
		// The device database (ADR-0026): one file in the private data directory, excluded from backup, with the fields that hold
		// personal data encrypted by a key that only this phone can read.
		services.AddSingleton<IDeviceKeyStore, SecureStorageDeviceKeyStore>();
		services.AddDeviceDatabase(Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName));

		// The key-value store of the settings (ADR-0004): the old card in the encrypted storage, everything else in the preferences.
		// It still serves the values that stay in the preferences and the copy of the previous version's data, which runs before
		// the database exists.
		services.AddSingleton(provider => new SettingsStore(
			new SecureValueStorage(provider.GetRequiredService<ILogger<SecureValueStorage>>()),
			new PreferencesValueStorage()));

		// What the screens and handlers read: the card, car and timer from the database, the rest from the key-value store.
		services.AddSingleton<DeviceDataResetNotice>();
		services.AddSingleton<ISettingsStore>(provider => new DeviceSettingsStore(
			provider.GetRequiredService<SettingsStore>(),
			provider.GetRequiredService<IResidentCardRepository>(),
			provider.GetRequiredService<IParkingSessionRepository>(),
			provider.GetRequiredService<ISavedPlacesRepository>(),
			provider.GetRequiredService<IVehicleRepository>(),
			provider.GetRequiredService<IDeviceUnitOfWork>(),
			provider.GetRequiredService<IDeviceDatabaseInitializer>(),
			provider.GetRequiredService<IStartupGate>(),
			provider.GetRequiredService<IReferenceData>(),
			provider.GetRequiredService<ILogger<DeviceSettingsStore>>(),
			provider.GetRequiredService<DeviceDataResetNotice>()));

		// The Home and Work places of the shortcut chips, in the same database.
		services.AddSingleton<IPlacesStore>(provider => new DevicePlacesStore(
			provider.GetRequiredService<ISavedPlacesRepository>(),
			provider.GetRequiredService<IResidentCardRepository>(),
			provider.GetRequiredService<IVehicleRepository>(),
			provider.GetRequiredService<IParkingSessionRepository>(),
			provider.GetRequiredService<IDeviceUnitOfWork>(),
			provider.GetRequiredService<IDeviceDatabaseInitializer>(),
			provider.GetRequiredService<IStartupGate>(),
			provider.GetRequiredService<ILogger<DevicePlacesStore>>(),
			provider.GetRequiredService<DeviceDataResetNotice>()));

		// The vehicle profile of the My vehicle dialog, in the same database.
		services.AddSingleton<IVehicleStore>(provider => new DeviceVehicleStore(
			provider.GetRequiredService<IVehicleRepository>(),
			provider.GetRequiredService<ISavedPlacesRepository>(),
			provider.GetRequiredService<IResidentCardRepository>(),
			provider.GetRequiredService<IParkingSessionRepository>(),
			provider.GetRequiredService<IDeviceUnitOfWork>(),
			provider.GetRequiredService<IDeviceDatabaseInitializer>(),
			provider.GetRequiredService<IStartupGate>(),
			provider.GetRequiredService<TimeProvider>(),
			provider.GetRequiredService<ILogger<DeviceVehicleStore>>(),
			provider.GetRequiredService<DeviceDataResetNotice>()));
	}

	/// <summary>
	/// Registers the start-up gate and the two copies it runs: the previous version's data, then the move into the device database.
	/// </summary>
	/// <param name="services">The service collection.</param>
	private static void AddStartupGate(IServiceCollection services)
	{
		// One run of the copy of the previous version's data (ADR-0004, ADR-0008); the Blazor screens wait for it before they read the
		// settings. The LevelDB files are read first, and a hidden web view is the fallback when they cannot be parsed.
		services.AddSingleton(provider => new LegacyDataMigration(
			new CompositeLegacyStorageReader(
				new LegacyLocalStorageReader(LegacyStorageDirectory(), provider.GetRequiredService<ILogger<LegacyLocalStorageReader>>()),
				new WebViewLegacyStorageReader(provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<WebViewLegacyStorageReader>>()),
				provider.GetRequiredService<ILogger<CompositeLegacyStorageReader>>()),
			provider.GetRequiredService<SettingsStore>(),
			new PreferencesValueStorage(),
			provider.GetRequiredService<TimeProvider>(),
			provider.GetRequiredService<ILogger<LegacyDataMigration>>()));

		// The start-up gate: the copy above, then the schema of the device database, then the move of the card, car and timer into it.
		services.AddSingleton(provider => new SettingsToDatabaseMigration(
			new SecureValueStorage(provider.GetRequiredService<ILogger<SecureValueStorage>>()),
			new PreferencesValueStorage(),
			provider.GetRequiredService<IResidentCardRepository>(),
			provider.GetRequiredService<IParkingSessionRepository>(),
			provider.GetRequiredService<IDeviceUnitOfWork>(),
			provider.GetRequiredService<IReferenceData>(),
			provider.GetRequiredService<TimeProvider>(),
			provider.GetRequiredService<ILogger<SettingsToDatabaseMigration>>()));
		services.AddSingleton<StartupGate>();
		services.AddSingleton<IStartupGate>(provider => provider.GetRequiredService<StartupGate>());
	}

	/// <summary>
	/// Registers the services of the phone: location, permissions, links, the card reader, parking alerts, visibility, connection and
	/// vibration.
	/// </summary>
	/// <param name="services">The service collection.</param>
	private static void AddDeviceServices(IServiceCollection services)
	{
		services.AddSingleton<ILocationTracker, FusedLocationTracker>();
		services.AddSingleton<ILocationPermissions, MauiLocationPermissions>();
		services.AddSingleton<ILinkLauncher, MauiLinkLauncher>();
		services.AddSingleton<ICardTextReader, MlKitCardTextReader>();

		// Parking alerts (ADR-0002): notifications, exact alarms for the timer and the foreground service. Singletons, because the
		// service and the receivers live outside the scope of the web view and reach them from the root provider.
		services.AddSingleton<INotifier, AndroidNotifier>();
		services.AddSingleton<IAlarmScheduler, AndroidAlarmScheduler>();
		services.AddSingleton<ParkingServiceState>();
		services.AddSingleton<IParkingWatchService, AndroidParkingWatchService>();

		// One instance behind two names: the application records the visibility, the Application layer reads it.
		services.AddSingleton<AppVisibility>();
		services.AddSingleton<IAppVisibility>(provider => provider.GetRequiredService<AppVisibility>());

		// One instance behind two names: the relay needs the change event, the view models the port.
		services.AddSingleton<ConnectivityNetworkStatus>();
		services.AddSingleton<INetworkStatus>(provider => provider.GetRequiredService<ConnectivityNetworkStatus>());
		services.AddSingleton<AppResumeNotifier>();
		services.TryAddSingleton<IVibration>(Vibration.Default);
		services.AddSingleton<CoverageVibrator>();
	}

	/// <summary>
	/// Registers what the sign-in needs from the phone: the address of the API of this build, the encrypted store of the session tokens
	/// and Google sign-in through Credential Manager (ADR-0014, ADR-0015). The session client itself is registered by the Infrastructure layer.
	/// </summary>
	/// <param name="services">The service collection.</param>
	private static void AddAccountServices(IServiceCollection services)
	{
		// Replaces the production address the Infrastructure layer registers by default.
		services.AddSingleton(new SessionApiSettings(new Uri(SignInConfiguration.ApiBaseUrl)));

		// The tokens live in the Android keystore storage, never in the device database.
		services.AddSingleton<ISessionTokenStore>(provider => new SecureSessionTokenStore(
			new SecureValueStorage(provider.GetRequiredService<ILogger<SecureValueStorage>>())));
		services.AddSingleton<IGoogleSignIn>(provider => new AndroidGoogleSignIn(
			SignInConfiguration.GoogleWebClientId,
			provider.GetRequiredService<ILogger<AndroidGoogleSignIn>>()));
	}

	/// <summary>
	/// Finds the <c>Local Storage/leveldb</c> directory of the web view of the Capacitor version: <c>app_webview</c> lies next to the
	/// <c>files</c> directory that <see cref="FileSystem.AppDataDirectory"/> names.
	/// </summary>
	/// <returns>The path; the directory does not exist on a new install.</returns>
	private static string LegacyStorageDirectory()
	{
		var dataDirectory = Path.GetDirectoryName(FileSystem.AppDataDirectory) ?? FileSystem.AppDataDirectory;
		return Path.Combine(dataDirectory, "app_webview", "Default", "Local Storage", "leveldb");
	}
	#endregion
}
