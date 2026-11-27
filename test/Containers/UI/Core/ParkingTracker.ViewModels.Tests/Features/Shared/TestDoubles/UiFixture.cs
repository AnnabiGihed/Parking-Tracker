using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.ProfileSync;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Features.Vehicles;
using ParkingTracker.ViewModels.Features.Welcome;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds every view model over substitutes for the ports and a fake clock, so that a test changes only the answer it is
///              about. The ports answer with calm defaults: online, nothing saved, nothing allowed or denied yet.
/// </summary>
internal sealed class UiFixture : IDisposable
{
	#region Static Instances
	/// <summary>
	/// The moment every test starts at: Tuesday 17 November 2026, 10:00, in the UTC time zone.
	/// </summary>
	public static readonly DateTimeOffset Start = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// A place in the Louise area of Brussels.
	/// </summary>
	public static readonly GeoPoint Here = new(50.8250, 4.3606);
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="UiFixture"/> class with every view model built.
	/// </summary>
	/// <param name="configureSettings">Sets up the settings store before any view model reads it, for example a saved car; <see langword="null"/> keeps the calm defaults.</param>
	public UiFixture(Action<ISettingsStore>? configureSettings = null)
	{
		Clock = new FakeTimeProvider(Start);
		Network.IsOnline.Returns(true);
		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Unknown, PermissionState.Unknown));
		Settings.GetLayersAsync().Returns(new LayerSettings());
		Settings.GetAlertsEnabledAsync().Returns(true);
		configureSettings?.Invoke(Settings);
		ReferenceData.GetReferenceData().Returns(Result.Success(Data()));
		Toast = new ToastViewModel(Clock);
		Permission = new LocationPermissionViewModel(Permissions, Settings, Clock, Toast);
		Welcome = new WelcomeViewModel(Settings, Permission);
		Card = new CardViewModel(Sender, Settings, ReferenceData, Launcher, Clock);
		Nearest = new NearestSpotViewModel(Sender, Launcher, Clock);
		Spot = new SpotCheckViewModel(Sender, Tracker, Clock, Toast, Nearest, Card);
		Search = new SearchViewModel(Sender, Spot, Toast);
		Notifier.RequestPermissionAsync(Arg.Any<CancellationToken>()).Returns(true);
		Notifier.AreAllowedAsync(Arg.Any<CancellationToken>()).Returns(true);
		Alarms.ScheduleAsync(Arg.Any<IReadOnlyList<TimerReminder>>(), Arg.Any<CancellationToken>()).Returns(AlarmPrecision.Exact);
		Car = new CarViewModel(Settings, Launcher, Spot, Toast, Clock, Alarms, Notifier, AlertCoordinator);
		Layers = new MapLayersViewModel(Sender, Settings, Network, Launcher, Clock);
		PlacesStore.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([]);
		VehicleStore.GetAsync(Arg.Any<CancellationToken>()).Returns((VehicleDetails?)null);
		Places = new PlacesViewModel(PlacesStore, Spot, Toast);
		Vehicle = new VehicleViewModel(VehicleStore, Toast, Clock);
		Wiper.WipeProfileAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		Wiper.WipeAllAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		Wiper.WipeProfileIfSavedAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(true));
		Owner.GetAccountHashAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<string>(DeviceDataErrors.NotStored, ResultExceptionType.NotFound));
		Owner.BindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		Owner.GetErasePendingAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(false));
		Owner.MarkErasePendingAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
		Sync.State.Returns(ProfileSyncState.Synced);
		Sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(new ProfileSyncResult(true, ProfileSyncState.Synced));
		LocalProfile.ReadAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(WithAHome));
		Eraser = new LocalDataEraser(Wiper, ResetNotice, Places, Vehicle, Card, Car, Alarms, NullLogger<LocalDataEraser>.Instance);
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the fake clock every view model reads; a test moves it with <c>Advance</c>.
	/// </summary>
	public FakeTimeProvider Clock { get; }

	/// <summary>
	/// Gets the substitute for the mediator that carries every query; a test sets the answer of a query on it.
	/// </summary>
	public ISender Sender { get; } = Substitute.For<ISender>();

	/// <summary>
	/// Gets the substitute for the position source of the device; <see cref="RaiseFix"/> and <see cref="RaiseLocationError"/> raise its events.
	/// </summary>
	public ILocationTracker Tracker { get; } = Substitute.For<ILocationTracker>();

	/// <summary>
	/// Gets the substitute for the location permissions of the device, which answers "unknown" until a test says otherwise.
	/// </summary>
	public ILocationPermissions Permissions { get; } = Substitute.For<ILocationPermissions>();

	/// <summary>
	/// Gets the substitute for the saved settings: the card, the car, the layer switches and the welcome flag. It saves nothing and answers "nothing saved".
	/// </summary>
	public ISettingsStore Settings { get; } = Substitute.For<ISettingsStore>();

	/// <summary>
	/// Gets the substitute for the opener of web, phone and navigation links; a test checks which address was opened.
	/// </summary>
	public ILinkLauncher Launcher { get; } = Substitute.For<ILinkLauncher>();

	/// <summary>
	/// Gets the substitute for the reference data (municipalities and sectors) that the card dialog lists; it answers a small fixed set.
	/// </summary>
	public IReferenceData ReferenceData { get; } = Substitute.For<IReferenceData>();

	/// <summary>
	/// Gets the substitute for the connection status; it says the device is online.
	/// </summary>
	public INetworkStatus Network { get; } = Substitute.For<INetworkStatus>();

	/// <summary>
	/// Gets the real toast that shows the short messages of the screens; a test reads its <c>Message</c>.
	/// </summary>
	public ToastViewModel Toast { get; }

	/// <summary>
	/// Gets the real view model of the location permission flow, over the permissions and settings substitutes.
	/// </summary>
	public LocationPermissionViewModel Permission { get; }

	/// <summary>
	/// Gets the real view model of the first-launch welcome dialog.
	/// </summary>
	public WelcomeViewModel Welcome { get; }

	/// <summary>
	/// Gets the real view model of the parking card dialog and the card line.
	/// </summary>
	public CardViewModel Card { get; }

	/// <summary>
	/// Gets the real view model of the nearest free spot block of the bottom sheet.
	/// </summary>
	public NearestSpotViewModel Nearest { get; }

	/// <summary>
	/// Gets the real view model of the main screen, which follows the position or the pin and checks the spot.
	/// </summary>
	public SpotCheckViewModel Spot { get; }

	/// <summary>
	/// Gets the real view model of the address search bar, which shows its result through <see cref="Spot"/>.
	/// </summary>
	public SearchViewModel Search { get; }

	/// <summary>
	/// Gets the real view model of the "My car" dialog, which saves the place of the parked car.
	/// </summary>
	public CarViewModel Car { get; }

	/// <summary>
	/// Gets the substitute for the alarms that show the timer reminders; it schedules exact alarms.
	/// </summary>
	public IAlarmScheduler Alarms { get; } = Substitute.For<IAlarmScheduler>();

	/// <summary>
	/// Gets the substitute for the background service of the parking alerts; it is not running.
	/// </summary>
	public IParkingWatchService WatchService { get; } = Substitute.For<IParkingWatchService>();

	/// <summary>
	/// Gets the substitute for the notifications; it allows them.
	/// </summary>
	public INotifier Notifier { get; } = Substitute.For<INotifier>();

	/// <summary>
	/// Gets the substitute for the coordinator of the background service.
	/// </summary>
	public IParkingAlertCoordinator AlertCoordinator { get; } = Substitute.For<IParkingAlertCoordinator>();

	/// <summary>
	/// Gets the real view model of the map layers, over the mediator, settings and network substitutes.
	/// </summary>
	public MapLayersViewModel Layers { get; }

	/// <summary>
	/// Gets the substitute for the saved places store, which holds no place.
	/// </summary>
	public IPlacesStore PlacesStore { get; } = Substitute.For<IPlacesStore>();

	/// <summary>
	/// Gets the substitute for the vehicle store, which holds no vehicle.
	/// </summary>
	public IVehicleStore VehicleStore { get; } = Substitute.For<IVehicleStore>();

	/// <summary>
	/// Gets the real view model of the saved places, over <see cref="PlacesStore"/>.
	/// </summary>
	public PlacesViewModel Places { get; }

	/// <summary>
	/// Gets the real view model of the vehicle profile, over <see cref="VehicleStore"/>.
	/// </summary>
	public VehicleViewModel Vehicle { get; }

	/// <summary>
	/// Gets the substitute for the wiper of the account data, which succeeds until a test says otherwise.
	/// </summary>
	public IDeviceDataWiper Wiper { get; } = Substitute.For<IDeviceDataWiper>();

	/// <summary>
	/// Gets the substitute for the store of the device owner, which holds no owner.
	/// </summary>
	public IDeviceOwnerStore Owner { get; } = Substitute.For<IDeviceOwnerStore>();

	/// <summary>
	/// Gets the notice that tells the stores about a wipe.
	/// </summary>
	public DeviceDataResetNotice ResetNotice { get; } = new();

	/// <summary>
	/// Gets the real eraser of the account data, over <see cref="Wiper"/> and the view models above.
	/// </summary>
	public LocalDataEraser Eraser { get; }

	/// <summary>
	/// Gets the real record of whether the data on the phone belongs to the signed-in account.
	/// </summary>
	public OwnerVerification Verification { get; } = new();

	/// <summary>
	/// Gets the substitute for the profile sync, which ends synced until a test says otherwise.
	/// </summary>
	public IProfileSync Sync { get; } = Substitute.For<IProfileSync>();

	/// <summary>
	/// Gets the substitute for the local profile, which holds a home place until a test says otherwise.
	/// </summary>
	public IProfileLocalStore LocalProfile { get; } = Substitute.For<IProfileLocalStore>();

	/// <summary>
	/// Gets a profile that holds one home place, so that the phone has something worth protecting.
	/// </summary>
	public static ProfileSnapshot WithAHome { get; } = new(
		new ProfileDocument(ProfileDocument.SupportedSchemaVersion, new ProfilePlaceSection("Home", 50.85, 4.35, "Rue", DateTimeOffset.UnixEpoch), null, new ProfileFavouritesSection(DateTimeOffset.UnixEpoch, []), null, null),
		SyncState.Empty);

	/// <summary>
	/// Gets a profile that holds nothing.
	/// </summary>
	public static ProfileSnapshot Nothing { get; } = new(
		new ProfileDocument(ProfileDocument.SupportedSchemaVersion, null, null, new ProfileFavouritesSection(DateTimeOffset.UnixEpoch, []), null, null),
		SyncState.Empty);
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the account view model over the given client and Google sign-in, with the owner store and the eraser of this fixture.
	/// </summary>
	/// <param name="client">The session client.</param>
	/// <param name="google">Tells whether this build can sign in with Google.</param>
	/// <returns>The account view model.</returns>
	public AccountViewModel CreateAccount(ISessionClient client, IGoogleSignIn google) => new(client, google, Toast, Owner, Eraser, Verification, Sync, LocalProfile, Clock);

	/// <summary>
	/// Makes every spot check answer with a decision.
	/// </summary>
	/// <param name="decision">The decision to answer with.</param>
	public void AnswerChecks(SpotDecision decision)
	{
		Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(decision));
	}

	/// <summary>
	/// Makes every nearest-spot search answer with spots.
	/// </summary>
	/// <param name="spots">The spots to answer with, nearest first.</param>
	public void AnswerSearches(params FreeSpot[] spots)
	{
		Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<FreeSpot>>(spots));
	}

	/// <summary>
	/// Raises a fix on the tracker substitute.
	/// </summary>
	/// <param name="fix">The fix to deliver.</param>
	public void RaiseFix(GpsFix fix)
	{
		Tracker.FixReceived += Raise.EventWith(Tracker, new LocationFixEventArgs(fix));
	}

	/// <summary>
	/// Raises a location failure on the tracker substitute.
	/// </summary>
	/// <param name="message">What went wrong.</param>
	/// <param name="kind">Whether the tracker stopped or only has no position for now.</param>
	public void RaiseLocationError(string message, LocationErrorKind kind = LocationErrorKind.Stopped)
	{
		Tracker.ErrorOccurred += Raise.EventWith(Tracker, new LocationErrorEventArgs(message, kind));
	}

	/// <summary>
	/// Builds a fix at the current moment of the fake clock.
	/// </summary>
	/// <param name="point">Where the fix is; <see cref="Here"/> when omitted.</param>
	/// <param name="accuracy">The accuracy in metres.</param>
	/// <returns>The fix.</returns>
	public GpsFix FixNow(GeoPoint? point = null, double accuracy = 10) => new(point ?? Here, accuracy, null, Clock.GetUtcNow());

	/// <summary>
	/// Builds a decision for a spot that is in a zone, so that the nearest-spot search follows a paid answer.
	/// </summary>
	/// <param name="verdict">The verdict of the decision.</param>
	/// <returns>The decision.</returns>
	public static SpotDecision InZone(Verdict verdict)
	{
		var surface = new ZoneSurface("1", "Avenue Louise", "Louizalaan", "IXELLES", 1, null, new SurfaceShape([]));
		return SpotDecision.Create(verdict, "detail", new ZoneReading(surface, null, null));
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		Car.Dispose();
		Spot.Dispose();
		Layers.Dispose();
		Toast.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the reference data: the City of Brussels with one sector, a municipality with areas and one without.
	/// </summary>
	/// <returns>The reference data.</returns>
	private static ReferenceDataSet Data()
	{
		var city = new Municipality("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null);
		var ixelles = new Municipality("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR"], ["LIM_X"], null);
		var etterbeek = new Municipality("etterbeek", "Etterbeek", "Etterbeek", "ETTERBEEK", MunicipalityKind.Whole, [], [], "No sectors.");
		var louise = new SectorDefinition("louise", "Louise", "Louiza", null, null, [], [], []);
		var tolerances = louise with { Tolerances = [new ToleranceAgreement("IXL_LOUISE", "Ixelles - Louise", "ixelles", [])] };
		return new ReferenceDataSet([city, ixelles, etterbeek], [louise], [tolerances]);
	}
	#endregion
}
