using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Infrastructure.Persistence.PostgreSQL.Extensions;

namespace ParkingTracker.Persistence.EntityFrameworkCore;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the server database context for a host: Pivot's PostgreSQL registration plus NetTopologySuite, so the
///              spatial types that the next stages store map to PostGIS geometry. Hosts never migrate (ADR-0023); the migration
///              bundle does.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
	#region Public Methods
	/// <summary>
	/// Registers <see cref="ParkingDataDbContext"/> on the given connection string.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="connectionString">The PostgreSQL connection string.</param>
	/// <returns>The same service collection.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/> or blank.</exception>
	public static IServiceCollection AddParkingDataPersistence(this IServiceCollection services, string connectionString)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		_ = services.AddPostgreSqlContext<ParkingDataDbContext>(
			connectionString,
			options => options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
		_ = services.AddScoped<IUserAccountRepository>(provider => new UserAccountRepository(provider.GetRequiredService<ParkingDataDbContext>()));
		_ = services.AddScoped<IAccountProfileRepository>(provider => new AccountProfileRepository(provider.GetRequiredService<ParkingDataDbContext>()));
		_ = services.AddScoped<IUnitOfWork>(provider => new ParkingDataUnitOfWork(provider.GetRequiredService<ParkingDataDbContext>(), provider.GetRequiredService<ILogger<ParkingDataUnitOfWork>>()));
		_ = services.AddScoped<ISignInChallengeStore>(provider => new SignInChallengeStore(provider.GetRequiredService<ParkingDataDbContext>()));
		_ = services.AddScoped<IErasureLog>(provider => new ErasureLog(provider.GetRequiredService<ParkingDataDbContext>()));
		return services;
	}
	#endregion
}
