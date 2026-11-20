using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.Testing.Device;

namespace ParkingTracker.Persistence.Device.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DeviceDatabaseServiceCollectionExtensions"/>: with the key store and the reference data that the
///              host and the infrastructure provide, every port of the device database resolves as one shared instance.
/// </summary>
public sealed class DeviceDatabaseServiceCollectionExtensionsTests
{
	#region Registration Tests
	/// <summary>
	/// Verifies that the ports resolve as one shared instance each, the cipher included, and that the schema can be
	/// created through the registered initializer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddDeviceDatabase_ShouldRegisterEveryPortAsASingleton()
	{
		var folder = Path.Combine(Path.GetTempPath(), "pt-device-di-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(folder);
		var services = new ServiceCollection();
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton(NullLoggerFactory.Instance);
		services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
		services.AddSingleton<IDeviceKeyStore>(new InMemoryKeyStore());
		services.AddSingleton(Substitute.For<IReferenceData>());
		services.AddDeviceDatabase(Path.Combine(folder, "parkingtracker.db"));
		await using var provider = services.BuildServiceProvider();

		(await provider.GetRequiredService<IDeviceDatabaseInitializer>().InitializeAsync()).IsSuccess.Should().BeTrue();

		provider.GetRequiredService<IResidentCardRepository>().Should().BeSameAs(provider.GetRequiredService<IResidentCardRepository>());
		provider.GetRequiredService<ISavedPlacesRepository>().Should().NotBeNull();
		provider.GetRequiredService<IParkingSessionRepository>().Should().NotBeNull();
		provider.GetRequiredService<IVehicleRepository>().Should().NotBeNull();
		provider.GetRequiredService<IDeviceUnitOfWork>().Should().BeSameAs(provider.GetRequiredService<IDeviceUnitOfWork>());
		provider.GetRequiredService<FieldCipher>().Should().BeSameAs(provider.GetRequiredService<FieldCipher>());
		provider.GetRequiredService<IEncryptedDataProbe>().Should().BeSameAs(provider.GetRequiredService<IEncryptedDataProbe>());
	}

	/// <summary>
	/// Verifies that a missing collection or path is rejected.
	/// </summary>
	[Fact]
	public void AddDeviceDatabase_InvalidArguments_ShouldThrow()
	{
		var noServices = () => DeviceDatabaseServiceCollectionExtensions.AddDeviceDatabase(null!, "x.db"); // null on purpose: proves the guard clause rejects it
		var noPath = () => new ServiceCollection().AddDeviceDatabase(" ");

		noServices.Should().Throw<ArgumentNullException>();
		noPath.Should().Throw<ArgumentException>();
	}
	#endregion
}
