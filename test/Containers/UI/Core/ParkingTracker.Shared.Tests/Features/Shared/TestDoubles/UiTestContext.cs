using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The bUnit context every component test starts from: the installers of the view models and of the screens, over
///              substitutes for the ports and a fake clock, and the JavaScript interop in strict mode, so that a call to map.js that a
///              test did not expect fails the test.
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
		Network.IsOnline.Returns(true);
		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Unknown, PermissionState.Unknown));
		Settings.GetLayersAsync().Returns(new LayerSettings());
		ReferenceData.GetReferenceData().Returns(Result.Success(Data()));
		Services.AddSingleton<TimeProvider>(Clock);
		Services.AddSingleton(Sender);
		Services.AddSingleton(Tracker);
		Services.AddSingleton(Permissions);
		Services.AddSingleton(Settings);
		Services.AddSingleton(Launcher);
		Services.AddSingleton(ReferenceData);
		Services.AddSingleton(Network);
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
