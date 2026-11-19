using Aspire.Hosting.ApplicationModel;

namespace ParkingTracker.AppHost;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The local composition of the back end (development only; the server runs the images with Compose, ADR-0023). It
///              describes resources and their order, and holds no secret: PostgreSQL with PostGIS (image postgis/postgis, the line
///              the server runs), the database "parkingdata", a one-shot migration through <c>dotnet ef database update</c> that the
///              API waits for (hosts never migrate), and the API with a link to its Scalar reference. The database password is a
///              generated parameter kept in the user secrets of this project, so the data volume keeps working across restarts.
/// </summary>
public static class Program
{
	#region Constants
	/// <summary>
	/// The connection name the API and the migration read: <c>ConnectionStrings:ParkingData</c>.
	/// </summary>
	private const string ConnectionName = "ParkingData";

	/// <summary>
	/// The project that owns the migrations, relative to the repository root.
	/// </summary>
	private const string PersistenceProject = "src/Infrastructure/ParkingTracker.Persistence.EntityFrameworkCore";
	#endregion

	#region Public Methods
	/// <summary>
	/// Describes the resources and runs the application.
	/// </summary>
	/// <param name="args">The command-line arguments.</param>
	public static void Main(string[] args)
	{
		var builder = DistributedApplication.CreateBuilder(args);

		var password = builder.AddParameter(
			"postgres-password",
			new GenerateParameterDefault { MinLength = 24, Special = false },
			secret: true,
			persist: true);
		var postgres = builder.AddPostgres("postgres", password: password)
			.WithImage("postgis/postgis")
			.WithImageTag("17-3.5")
			.WithDataVolume()
			.WithLifetime(ContainerLifetime.Persistent);
		var parkingData = postgres.AddDatabase("parkingdata");

		// The tool manifest lives at the repository root, so the command runs from there.
		var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "..", ".."));
		var migrate = builder.AddExecutable(
				"migrate",
				"dotnet",
				repositoryRoot,
				"ef",
				"database",
				"update",
				"--project",
				PersistenceProject)
			.WithReference(parkingData, connectionName: ConnectionName)
			.WaitFor(parkingData);

		_ = builder.AddProject<Projects.ParkingTracker_ApiService>("api")
			.WithReference(parkingData, connectionName: ConnectionName)
			.WaitForCompletion(migrate)
			.WithUrlForEndpoint("https", endpoint => new ResourceUrlAnnotation { Url = "/scalar", DisplayText = "API reference" });

		builder.Build().Run();
	}
	#endregion
}
