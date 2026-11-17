using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
