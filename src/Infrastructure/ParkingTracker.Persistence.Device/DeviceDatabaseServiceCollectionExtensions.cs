using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Persistence.Device.Features.Accounts;
using ParkingTracker.Persistence.Device.Features.ParkingSessions;
using ParkingTracker.Persistence.Device.Features.ResidentCards;
using ParkingTracker.Persistence.Device.Features.SavedPlaces;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.Persistence.Device.Features.Vehicles;

namespace ParkingTracker.Persistence.Device;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the device database (ADR-0026) in the service collection of the host: the connection to the database
///              file, the schema initializer, the unit of work, the field cipher and one repository per aggregate, the store of
///              the device owner and the wiper of the account data. Everything is a singleton, because the phone has one database
///              and one connection per process. The host adds the one thing that only the platform can provide, its
///              <see cref="IDeviceKeyStore"/>.
/// </summary>
public static class DeviceDatabaseServiceCollectionExtensions
{
	#region Public Methods
	/// <summary>
	/// Registers the device database over the file at <paramref name="databasePath"/>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="databasePath">The full path of the database file, in the private data directory of the app.</param>
	/// <returns>The same service collection.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="databasePath"/> is <see langword="null"/> or blank.</exception>
	public static IServiceCollection AddDeviceDatabase(this IServiceCollection services, string databasePath)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
		services.AddSingleton(new DeviceDatabase(databasePath));
		services.AddSingleton<IDeviceDatabaseInitializer, DeviceDatabaseInitializer>();
		services.AddSingleton<IDeviceUnitOfWork, DeviceUnitOfWork>();
		services.AddSingleton<IEncryptedDataProbe, EncryptedDataProbe>();
		services.AddSingleton<FieldCipher>();
		services.AddSingleton<IResidentCardRepository, ResidentCardRepository>();
		services.AddSingleton<ISavedPlacesRepository, SavedPlacesRepository>();
		services.AddSingleton<IParkingSessionRepository, ParkingSessionRepository>();
		services.AddSingleton<IVehicleRepository, VehicleRepository>();
		services.AddSingleton<IDeviceOwnerStore, DeviceOwnerStore>();
		services.AddSingleton<IDeviceDataWiper, DeviceDataWiper>();
		return services;
	}
	#endregion
}
