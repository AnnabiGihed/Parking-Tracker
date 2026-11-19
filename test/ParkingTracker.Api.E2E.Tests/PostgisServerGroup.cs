using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Makes every test class that needs the database share one PostgreSQL container, which takes seconds to start; each
///              test still works in a database of its own.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgisServerGroup : ICollectionFixture<PostgisServer>
{
	#region Constants
	/// <summary>
	/// The name that test classes put in their <c>Collection</c> attribute.
	/// </summary>
	public const string Name = "postgis";
	#endregion
}
