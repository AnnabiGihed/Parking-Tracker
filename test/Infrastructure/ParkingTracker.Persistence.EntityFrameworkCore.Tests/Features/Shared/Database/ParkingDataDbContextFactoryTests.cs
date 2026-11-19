using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Tests.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="ParkingDataDbContextFactory"/>: the connection string comes from the file first, then from the
///              variable, and a missing or unreadable source stops with a message that says what to set. The migration bundle on
///              the server depends on this order.
/// </summary>
public sealed class ParkingDataDbContextFactoryTests
{
	#region Constants
	/// <summary>
	/// A connection string with no real credentials, for the tests that only pass it through.
	/// </summary>
	private const string SampleConnection = "Host=db;Database=parking";
	#endregion

	#region Resolution Tests
	/// <summary>
	/// Verifies that the file named by the file variable wins over the connection string variable and is trimmed.
	/// </summary>
	[Fact]
	public void ResolveConnectionString_FileAndVariable_ShouldPreferTheFile()
	{
		var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[ParkingDataDbContextFactory.ConnectionFileVariable] = "/run/secrets/x",
			[ParkingDataDbContextFactory.ConnectionStringVariable] = "Host=other",
		};

		var result = ParkingDataDbContextFactory.ResolveConnectionString(name => variables.GetValueOrDefault(name), _ => SampleConnection + "\n");

		result.Should().Be(SampleConnection);
	}

	/// <summary>
	/// Verifies that the connection string variable is used when no file is named.
	/// </summary>
	[Fact]
	public void ResolveConnectionString_OnlyVariable_ShouldUseTheVariable()
	{
		var variables = new Dictionary<string, string?>(StringComparer.Ordinal) { [ParkingDataDbContextFactory.ConnectionStringVariable] = SampleConnection };

		var result = ParkingDataDbContextFactory.ResolveConnectionString(name => variables.GetValueOrDefault(name), _ => throw new InvalidOperationException("no file expected"));

		result.Should().Be(SampleConnection);
	}

	/// <summary>
	/// Verifies that having neither source fails with a message that names both variables.
	/// </summary>
	[Fact]
	public void ResolveConnectionString_NoSource_ShouldNameBothVariables()
	{
		var act = () => ParkingDataDbContextFactory.ResolveConnectionString(_ => null, _ => string.Empty);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{ParkingDataDbContextFactory.ConnectionFileVariable}*{ParkingDataDbContextFactory.ConnectionStringVariable}*");
	}

	/// <summary>
	/// Verifies that an empty file fails instead of falling back to the variable, which would hide a broken secret.
	/// </summary>
	[Fact]
	public void ResolveConnectionString_EmptyFile_ShouldThrow()
	{
		var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[ParkingDataDbContextFactory.ConnectionFileVariable] = "/run/secrets/x",
			[ParkingDataDbContextFactory.ConnectionStringVariable] = SampleConnection,
		};

		var act = () => ParkingDataDbContextFactory.ResolveConnectionString(name => variables.GetValueOrDefault(name), _ => "  \n");

		act.Should().Throw<InvalidOperationException>().WithMessage("*empty*");
	}

	/// <summary>
	/// Verifies that a file that cannot be read fails with the path in the message and the cause attached.
	/// </summary>
	[Fact]
	public void ResolveConnectionString_UnreadableFile_ShouldThrowWithPath()
	{
		var variables = new Dictionary<string, string?>(StringComparer.Ordinal) { [ParkingDataDbContextFactory.ConnectionFileVariable] = "/run/secrets/missing" };

		var act = () => ParkingDataDbContextFactory.ResolveConnectionString(name => variables.GetValueOrDefault(name), _ => throw new FileNotFoundException());

		act.Should().Throw<InvalidOperationException>().WithMessage("*/run/secrets/missing*").WithInnerException<FileNotFoundException>();
	}
	#endregion
}
