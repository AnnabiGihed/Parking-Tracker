using Microsoft.EntityFrameworkCore;
using Pivot.Framework.Infrastructure.Persistence.EntityFrameworkCore.PersistenceContext;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The write-side database context of the server (ADR-0010). It declares the PostGIS extension and applies the entity
///              configurations of the assembly (so far the accounts with their sessions, the sign-in challenges and the erasure log;
///              the next aggregates arrive with the stages that need them). The extension itself is created by the migration, which
///              runs as the owner role; the API runs with a role that cannot create extensions.
/// </summary>
public sealed class ParkingDataDbContext : PivotDbContextBase
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingDataDbContext"/> class with the options the host or the design-time factory built.
	/// </summary>
	/// <param name="options">The context options.</param>
	public ParkingDataDbContext(DbContextOptions<ParkingDataDbContext> options)
		: base(options)
	{
	}
	#endregion

	#region Protected Methods
	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		_ = modelBuilder.HasPostgresExtension("postgis");
		_ = modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParkingDataDbContext).Assembly);
	}
	#endregion
}
