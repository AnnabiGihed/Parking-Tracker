using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds <see cref="ParkingDataDbContext"/> for the EF Core tools and the migration bundle, which cannot use the host.
///              The connection string comes from a file named by <see cref="ConnectionFileVariable"/> (the server keeps it in a
///              secret file) or else from the <see cref="ConnectionStringVariable"/> variable (the local AppHost). Nothing is
///              hard-coded, and a missing source stops with a message that names both.
/// </summary>
public sealed class ParkingDataDbContextFactory : IDesignTimeDbContextFactory<ParkingDataDbContext>
{
	#region Constants
	/// <summary>
	/// The environment variable holding the path of a file that contains the connection string.
	/// </summary>
	public const string ConnectionFileVariable = "PARKING_DATA_CONNECTION_FILE";

	/// <summary>
	/// The environment variable holding the connection string itself.
	/// </summary>
	public const string ConnectionStringVariable = "ConnectionStrings__ParkingData";
	#endregion

	#region Public Methods
	/// <inheritdoc />
	/// <exception cref="InvalidOperationException">No connection string is configured, or the file named for it cannot be read or is empty.</exception>
	public ParkingDataDbContext CreateDbContext(string[] args)
	{
		var connectionString = ResolveConnectionString(Environment.GetEnvironmentVariable, File.ReadAllText);
		var options = new DbContextOptionsBuilder<ParkingDataDbContext>()
			.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
			.Options;
		return new ParkingDataDbContext(options);
	}
	#endregion

	#region Internal Methods
	/// <summary>
	/// Finds the connection string: the file first, then the variable.
	/// </summary>
	/// <param name="getVariable">Reads an environment variable, <see langword="null"/> when it is not set.</param>
	/// <param name="readFile">Reads the text of a file.</param>
	/// <returns>The connection string, trimmed.</returns>
	/// <exception cref="InvalidOperationException">No source is configured, or the file cannot be read or is empty.</exception>
	internal static string ResolveConnectionString(Func<string, string?> getVariable, Func<string, string> readFile)
	{
		var file = getVariable(ConnectionFileVariable);
		if (!string.IsNullOrWhiteSpace(file))
		{
			return ReadFromFile(file, readFile);
		}

		var value = getVariable(ConnectionStringVariable);
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}

		throw new InvalidOperationException(
			$"No connection string for the parking database: set {ConnectionFileVariable} to the path of a file that holds it, or set {ConnectionStringVariable}.");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the connection string from a file, turning a read failure or an empty file into a message that names the file.
	/// </summary>
	/// <param name="path">The path of the file.</param>
	/// <param name="readFile">Reads the text of a file.</param>
	/// <returns>The connection string, trimmed.</returns>
	/// <exception cref="InvalidOperationException">The file cannot be read or is empty.</exception>
	private static string ReadFromFile(string path, Func<string, string> readFile)
	{
		string content;
		try
		{
			content = readFile(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new InvalidOperationException($"The file named by {ConnectionFileVariable} cannot be read: {path}.", exception);
		}

		return string.IsNullOrWhiteSpace(content)
			? throw new InvalidOperationException($"The file named by {ConnectionFileVariable} is empty: {path}.")
			: content.Trim();
	}
	#endregion
}
