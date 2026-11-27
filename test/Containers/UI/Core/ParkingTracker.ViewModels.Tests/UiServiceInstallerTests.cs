using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.Menu;
using ParkingTracker.ViewModels.Features.ProfileSync;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="UiServiceInstaller"/> and <see cref="AssemblyReference"/>: the view models resolve once the
///              ports are registered, one instance per scope, and missing arguments are rejected.
/// </summary>
public sealed class UiServiceInstallerTests
{
	#region Install Tests
	/// <summary>
	/// Verifies that the screen's view models resolve and are shared inside a scope.
	/// </summary>
	[Fact]
	public void Install_ServicesRegistered_ShouldResolveViewModelsOncePerScope()
	{
		var services = new ServiceCollection();
		services.AddSingleton(Substitute.For<ISender>());
		services.AddSingleton(Substitute.For<ILocationTracker>());
		services.AddSingleton(Substitute.For<ILocationPermissions>());
		services.AddSingleton(Substitute.For<ISettingsStore>());
		services.AddSingleton(Substitute.For<ILinkLauncher>());
		services.AddSingleton(Substitute.For<IAlarmScheduler>());
		services.AddSingleton(Substitute.For<INotifier>());
		services.AddSingleton(Substitute.For<IParkingAlertCoordinator>());
		var referenceData = Substitute.For<IReferenceData>();
		referenceData.GetReferenceData().Returns(Result.Success(new ReferenceDataSet([], [], [])));
		services.AddSingleton(referenceData);
		services.AddSingleton(Substitute.For<INetworkStatus>());
		new UiServiceInstaller().Install(services, new ConfigurationBuilder().Build());
		using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();

		var first = scope.ServiceProvider.GetRequiredService<SpotCheckViewModel>();

		first.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<SpotCheckViewModel>());
		first.Nearest.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<NearestSpotViewModel>());
	}

	/// <summary>
	/// Verifies that the profile sync resolves with its trigger, the sign-in record, the account view model and the menu sharing one instance of each
	/// per scope, and that the screens reloader is the eraser.
	/// </summary>
	[Fact]
	public void Install_ProfileSyncRegistered_ShouldShareOneInstanceOfEachPerScope()
	{
		var services = new ServiceCollection();
		services.AddSingleton(Substitute.For<ISender>());
		services.AddSingleton(Substitute.For<ILocationTracker>());
		services.AddSingleton(Substitute.For<ILocationPermissions>());
		services.AddSingleton(Substitute.For<ISettingsStore>());
		services.AddSingleton(Substitute.For<ILinkLauncher>());
		services.AddSingleton(Substitute.For<IAlarmScheduler>());
		services.AddSingleton(Substitute.For<INotifier>());
		services.AddSingleton(Substitute.For<IParkingAlertCoordinator>());
		services.AddSingleton(Substitute.For<IReferenceData>());
		services.AddSingleton(Substitute.For<INetworkStatus>());
		services.AddSingleton(Substitute.For<IPlacesStore>());
		services.AddSingleton(Substitute.For<IVehicleStore>());
		services.AddSingleton(Substitute.For<ISessionClient>());
		services.AddSingleton(Substitute.For<IGoogleSignIn>());
		services.AddSingleton(Substitute.For<IDeviceOwnerStore>());
		services.AddSingleton(Substitute.For<IDeviceDataWiper>());
		services.AddSingleton(Substitute.For<IProfileClient>());
		services.AddSingleton(Substitute.For<IProfileLocalStore>());
		services.AddSingleton(Substitute.For<IProfileChangeTracker>());
		services.AddSingleton(Substitute.For<IStartupGate>());
		services.AddSingleton<DeviceDataResetNotice>();
		services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		new UiServiceInstaller().Install(services, new ConfigurationBuilder().Build());
		using var provider = services.BuildServiceProvider(validateScopes: true);
		using var scope = provider.CreateScope();

		var trigger = scope.ServiceProvider.GetRequiredService<ProfileSyncTrigger>();
		var sync = scope.ServiceProvider.GetRequiredService<IProfileSync>();

		trigger.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ProfileSyncTrigger>());
		sync.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ProfileSyncService>());
		scope.ServiceProvider.GetRequiredService<OwnerVerification>().Should().BeSameAs(scope.ServiceProvider.GetRequiredService<OwnerVerification>());
		scope.ServiceProvider.GetRequiredService<IScreenReloader>().Should().BeSameAs(scope.ServiceProvider.GetRequiredService<LocalDataEraser>());
		scope.ServiceProvider.GetRequiredService<MenuViewModel>().Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	[Fact]
	public void Install_NullArguments_ShouldThrowArgumentNullException()
	{
		var installer = new UiServiceInstaller();

		var noServices = () => installer.Install(null!, new ConfigurationBuilder().Build()); // null on purpose: proves the guard clause rejects it
		var noConfiguration = () => installer.Install(new ServiceCollection(), null!); // null on purpose: proves the guard clause rejects it

		noServices.Should().Throw<ArgumentNullException>();
		noConfiguration.Should().Throw<ArgumentNullException>();
	}

	/// <summary>
	/// Verifies that the reference points at the ViewModels assembly.
	/// </summary>
	[Fact]
	public void Assembly_Reference_ShouldBeTheViewModelsAssembly()
	{
		AssemblyReference.Assembly.GetName().Name.Should().Be("ParkingTracker.ViewModels");
	}
	#endregion
}
