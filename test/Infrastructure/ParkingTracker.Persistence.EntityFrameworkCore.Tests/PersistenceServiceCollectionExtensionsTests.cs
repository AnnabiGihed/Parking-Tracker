using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="PersistenceServiceCollectionExtensions"/>: the context resolves from the container, and bad
///              arguments are rejected before anything is registered. Resolving the context does not open a connection.
/// </summary>
public sealed class PersistenceServiceCollectionExtensionsTests
{
	#region Registration Tests
	/// <summary>
	/// Verifies that the database context resolves from the registered services.
	/// </summary>
	[Fact]
	public void AddParkingDataPersistence_ShouldRegisterTheContext()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddParkingDataPersistence("Host=localhost;Database=parking;Username=u;Password=p");
		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();

		var context = scope.ServiceProvider.GetRequiredService<ParkingDataDbContext>();

		context.Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a missing collection or a blank connection string is rejected.
	/// </summary>
	[Fact]
	public void AddParkingDataPersistence_InvalidArguments_ShouldThrow()
	{
		var noServices = () => PersistenceServiceCollectionExtensions.AddParkingDataPersistence(null!, "Host=x"); // null on purpose: proves the guard clause rejects it
		var noConnection = () => new ServiceCollection().AddParkingDataPersistence(" ");

		noServices.Should().Throw<ArgumentNullException>();
		noConnection.Should().Throw<ArgumentException>();
	}
	#endregion
}
