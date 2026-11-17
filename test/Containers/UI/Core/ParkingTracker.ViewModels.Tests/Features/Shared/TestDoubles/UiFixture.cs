using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;
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
		Settings.GetLayers().Returns(new LayerSettings());
		configureSettings?.Invoke(Settings);
		ReferenceData.GetReferenceData().Returns(Result.Success(Data()));
		Toast = new ToastViewModel(Clock);
		Permission = new LocationPermissionViewModel(Permissions, Settings, Clock, Toast);
		Welcome = new WelcomeViewModel(Settings, Permission);
		Card = new CardViewModel(Sender, Settings, ReferenceData, Launcher, Clock);
		Nearest = new NearestSpotViewModel(Sender, Launcher, Clock);
		Spot = new SpotCheckViewModel(Sender, Tracker, Clock, Toast, Nearest, Card);
		Search = new SearchViewModel(Sender, Spot, Toast);
		Car = new CarViewModel(Settings, Launcher, Spot, Toast, Clock);
		Layers = new MapLayersViewModel(Sender, Settings, Network, Launcher, Clock);
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
	/// Gets the real view model of the map layers, over the mediator, settings and network substitutes.
	/// </summary>
	public MapLayersViewModel Layers { get; }
	#endregion

	#region Public Methods
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
	public void RaiseLocationError(string message)
	{
		Tracker.ErrorOccurred += Raise.EventWith(Tracker, new LocationErrorEventArgs(message));
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
