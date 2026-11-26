using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Shared.Settings.Database;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;
using Radzen.Blazor;

namespace ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The bUnit context every component test starts from: the installers of the view models and of the screens (which
///              register the Radzen services), over substitutes for the ports and a fake clock, and the JavaScript interop in strict
///              mode with the calls of Radzen.Blazor.js answered, so that a call to map.js that a test did not expect fails the test.
/// </summary>
public abstract class UiTestContext : BunitContext
{
	#region Static Instances
	/// <summary>
	/// The moment every test starts at: Tuesday 17 November 2026, 10:00, in the UTC time zone.
	/// </summary>
	protected static readonly DateTimeOffset Start = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// A place in the Louise area of Brussels.
	/// </summary>
	protected static readonly GeoPoint Here = new(50.8250, 4.3606);
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="UiTestContext"/> class and registers the services.
	/// </summary>
	protected UiTestContext()
	{
		JSInterop.Mode = JSRuntimeMode.Strict;
		SetUpRadzenInterop();
		Network.IsOnline.Returns(true);
		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Unknown, PermissionState.Unknown));
		Settings.GetLayersAsync().Returns(new LayerSettings());
		ReferenceData.GetReferenceData().Returns(Result.Success(Data()));
		Services.AddSingleton<TimeProvider>(Clock);
		Services.AddSingleton(Sender);
		Services.AddSingleton(Tracker);
		Services.AddSingleton(Permissions);
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([]);
		Services.AddSingleton(Settings);
		Services.AddSingleton(Places);
		Vehicles.GetAsync(Arg.Any<CancellationToken>()).Returns((VehicleDetails?)null);
		Vehicles.SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		Services.AddSingleton(Vehicles);
		Services.AddSingleton(Launcher);
		Services.AddSingleton(ReferenceData);
		Services.AddSingleton(Network);
		Notifier.RequestPermissionAsync(Arg.Any<CancellationToken>()).Returns(true);
		Alarms.ScheduleAsync(Arg.Any<IReadOnlyList<TimerReminder>>(), Arg.Any<CancellationToken>()).Returns(AlarmPrecision.Exact);
		Settings.GetAlertsEnabledAsync().Returns(true);
		Google.IsAvailable.Returns(false);
		Services.AddSingleton(Google);
		Services.AddSingleton(Session);
		Services.AddSingleton(Alarms);
		Services.AddSingleton(Notifier);
		Services.AddSingleton(AlertCoordinator);

		// The migration of the previous version's data has finished before a screen is shown, unless a test says otherwise.
		LegacyPlain.ReadAsync(LegacyDataMigration.MarkerKey).Returns("{}");
		var legacy = new LegacyDataMigration(LegacyReader, Settings, LegacyPlain, Clock, NullLogger<LegacyDataMigration>.Instance);
		Services.AddSingleton(legacy);
		Services.AddSingleton(CreateStartupGate(legacy));
		var configuration = new ConfigurationBuilder().Build();
		new global::ParkingTracker.ViewModels.UiServiceInstaller().Install(Services, configuration);
		new global::ParkingTracker.Shared.UiServiceInstaller().Install(Services, configuration);
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the fake clock.
	/// </summary>
	protected FakeTimeProvider Clock { get; } = new(Start);

	/// <summary>
	/// Gets the setup of the call that takes the focus off the text box; a test can make it fail with <c>SetException</c>.
	/// </summary>
	protected JSRuntimeInvocationHandler Blur { get; private set; } = null!;

	/// <summary>
	/// Gets the mediator substitute.
	/// </summary>
	protected ISender Sender { get; } = Substitute.For<ISender>();

	/// <summary>
	/// Gets the location tracker substitute.
	/// </summary>
	protected ILocationTracker Tracker { get; } = Substitute.For<ILocationTracker>();

	/// <summary>
	/// Gets the permissions substitute.
	/// </summary>
	protected ILocationPermissions Permissions { get; } = Substitute.For<ILocationPermissions>();

	/// <summary>
	/// Gets the settings store substitute.
	/// </summary>
	protected ISettingsStore Settings { get; } = Substitute.For<ISettingsStore>();

	/// <summary>
	/// Gets the saved places store substitute, which holds no place.
	/// </summary>
	protected IPlacesStore Places { get; } = Substitute.For<IPlacesStore>();

	/// <summary>
	/// Gets the vehicle store substitute, which holds no vehicle and accepts every save.
	/// </summary>
	protected IVehicleStore Vehicles { get; } = Substitute.For<IVehicleStore>();

	/// <summary>
	/// Gets the Google sign-in substitute, which is not available (as in a build without a Web client ID) unless a test says otherwise.
	/// </summary>
	protected IGoogleSignIn Google { get; } = Substitute.For<IGoogleSignIn>();

	/// <summary>
	/// Gets the session client substitute.
	/// </summary>
	protected ISessionClient Session { get; } = Substitute.For<ISessionClient>();

	/// <summary>
	/// Gets the link launcher substitute.
	/// </summary>
	protected ILinkLauncher Launcher { get; } = Substitute.For<ILinkLauncher>();

	/// <summary>
	/// Gets the reference data substitute.
	/// </summary>
	protected IReferenceData ReferenceData { get; } = Substitute.For<IReferenceData>();

	/// <summary>
	/// Gets the network status substitute.
	/// </summary>
	protected INetworkStatus Network { get; } = Substitute.For<INetworkStatus>();

	/// <summary>
	/// Gets the alarm scheduler substitute, which schedules exact alarms.
	/// </summary>
	protected IAlarmScheduler Alarms { get; } = Substitute.For<IAlarmScheduler>();

	/// <summary>
	/// Gets the notifier substitute, which allows notifications.
	/// </summary>
	protected INotifier Notifier { get; } = Substitute.For<INotifier>();

	/// <summary>
	/// Gets the substitute for the reader of the previous version's storage.
	/// </summary>
	protected ILegacyStorageReader LegacyReader { get; } = Substitute.For<ILegacyStorageReader>();

	/// <summary>
	/// Gets the substitute for the preferences that hold the migration marker; the marker exists unless a test removes it.
	/// </summary>
	protected IKeyValueStorage LegacyPlain { get; } = Substitute.For<IKeyValueStorage>();

	/// <summary>
	/// Gets the substitute for the coordinator of the background service.
	/// </summary>
	protected IParkingAlertCoordinator AlertCoordinator { get; } = Substitute.For<IParkingAlertCoordinator>();
	#endregion

	#region Protected Methods
	/// <summary>
	/// Resolves a service the components share, such as a view model.
	/// </summary>
	/// <typeparam name="T">The type of the service.</typeparam>
	/// <returns>The service.</returns>
	protected T Resolve<T>()
		where T : notnull
	{
		return Services.GetRequiredService<T>();
	}

	/// <summary>
	/// Renders the Radzen dialog host and a dialog component that the <c>DialogBridge</c> opens in it, so the content of the dialog
	/// is found in the returned host while the view model is open.
	/// </summary>
	/// <typeparam name="TDialog">The dialog component.</typeparam>
	/// <returns>The Radzen dialog host, which holds the dialog while it is open.</returns>
	protected IRenderedComponent<RadzenDialog> RenderInDialogHost<TDialog>()
		where TDialog : IComponent
	{
		var host = Render<RadzenDialog>();
		Render<TDialog>();
		return host;
	}

	/// <summary>
	/// Makes every spot check answer with a decision.
	/// </summary>
	/// <param name="decision">The decision to answer with.</param>
	protected void AnswerChecks(SpotDecision decision)
	{
		Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(decision));
	}

	/// <summary>
	/// Makes every nearest-spot search answer with spots.
	/// </summary>
	/// <param name="spots">The spots to answer with, nearest first.</param>
	protected void AnswerSearches(params FreeSpot[] spots)
	{
		Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<FreeSpot>>(spots));
	}

	/// <summary>
	/// Builds a decision for a spot that is in a zone.
	/// </summary>
	/// <param name="verdict">The verdict of the decision.</param>
	/// <param name="detail">The explanation.</param>
	/// <returns>The decision.</returns>
	protected static SpotDecision InZone(Verdict verdict, string detail = "detail")
	{
		var surface = new ZoneSurface("1", "Avenue Louise", "Louizalaan", "IXELLES", 1, null, new SurfaceShape([]));
		return SpotDecision.Create(verdict, detail, new ZoneReading(surface, null, null));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the start-up gate over the given migration of the previous version's data, with a device database that is ready and
	/// whose move of the saved values has finished, so that only the legacy migration decides when the page opens.
	/// </summary>
	/// <param name="legacy">The migration of the previous version's data.</param>
	/// <returns>The gate.</returns>
	private StartupGate CreateStartupGate(LegacyDataMigration legacy)
	{
		var initializer = Substitute.For<IDeviceDatabaseInitializer>();
		initializer.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		var plain = Substitute.For<IKeyValueStorage>();
		plain.ReadAsync(SettingsToDatabaseMigration.MarkerKey).Returns("{}");
		var database = new SettingsToDatabaseMigration(Substitute.For<IKeyValueStorage>(), plain, Substitute.For<IResidentCardRepository>(), Substitute.For<IParkingSessionRepository>(), Substitute.For<IDeviceUnitOfWork>(), ReferenceData, Clock, NullLogger<SettingsToDatabaseMigration>.Instance);
		return new StartupGate(legacy, initializer, database, NullLogger<StartupGate>.Instance);
	}

	/// <summary>
	/// Answers the fire-and-forget calls that the Radzen components make to <c>Radzen.Blazor.js</c> (global functions named
	/// <c>Radzen.*</c>, for example when a dialog or a tooltip opens). Only those calls are answered: strict mode stays on, so a call
	/// to <c>map.js</c> that a test did not set up still fails the test.
	/// </summary>
	private void SetUpRadzenInterop()
	{
		JSInterop.SetupVoid(invocation => invocation.Identifier.StartsWith("Radzen.", StringComparison.Ordinal)).SetVoidResult();
		_ = JSInterop.SetupModule(invocation => invocation.Identifier.StartsWith("Radzen.", StringComparison.Ordinal));

		// The calls that open or close a dialog, or open the side menu, answer with a string that the tests do not read.
		JSInterop.Setup<string>("Radzen.closeDialog", _ => true).SetResult(string.Empty);
		JSInterop.Setup<string>("Radzen.openDialog", _ => true).SetResult(string.Empty);
		JSInterop.Setup<string>("Radzen.openSideDialog", _ => true).SetResult(string.Empty);

		// The search bar takes the focus off the text box after a submit, to close the keyboard.
		Blur = JSInterop.SetupVoid("document.activeElement.blur");
		Blur.SetVoidResult();
	}

	/// <summary>
	/// Builds the reference data: the City of Brussels with one sector and a municipality with areas.
	/// </summary>
	/// <returns>The reference data.</returns>
	private static ReferenceDataSet Data()
	{
		var city = new Municipality("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null);
		var ixelles = new Municipality("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR"], ["LIM_X"], null);
		var louise = new SectorDefinition("louise", "Louise", "Louiza", null, null, [], [], []);
		var tolerances = louise with { Tolerances = [new ToleranceAgreement("IXL_LOUISE", "Ixelles - Louise", "ixelles", [])] };
		return new ReferenceDataSet([city, ixelles], [louise], [tolerances]);
	}
	#endregion
}
