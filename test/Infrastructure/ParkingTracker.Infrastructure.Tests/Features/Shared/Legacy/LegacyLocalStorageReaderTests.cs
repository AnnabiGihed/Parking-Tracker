using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Infrastructure.Features.Shared.Legacy;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.Shared.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="LegacyLocalStorageReader"/> against a temporary directory laid out like the web view's
///              <c>Local Storage/leveldb</c>: a missing directory, or one without database files, is a new install, a log and a table
///              are both read, and a corrupt table, or files without any <c>pt.*</c> key, are a failure (so that the fallback reader is
///              asked and the migration tries again) and not an empty result.
/// </summary>
public sealed class LegacyLocalStorageReaderTests : IDisposable
{
	#region Fields
	/// <summary>
	/// The directory of the test, deleted afterwards.
	/// </summary>
	private readonly string root = Path.Combine(Path.GetTempPath(), "pt-legacy-" + Guid.NewGuid().ToString("N"));
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		if (Directory.Exists(root))
		{
			Directory.Delete(root, true);
		}
	}
	#endregion

	#region Read Tests
	/// <summary>
	/// Verifies that a missing directory, as on a new install, is a successful read without values.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_MissingDirectory_ShouldSucceedWithoutValues()
	{
		var reading = await Create(Path.Combine(root, "none")).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
		reading.Values.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a directory with no database file (only <c>LOCK</c> and <c>CURRENT</c>) is a new install, not a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_DirectoryWithoutDatabaseFiles_ShouldSucceedWithoutValues()
	{
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "LOCK"), []);

		var reading = await Create(root).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
		reading.Values.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that database files without any <c>pt.*</c> key are a failure, so that the fallback reader gets its turn.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_FilesWithoutAnyKey_ShouldFail()
	{
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "000007.log"), LevelDbBuilder.Log((1, [(LevelDbBuilder.Key("other.key"), LevelDbBuilder.Value("x"))])));

		var reading = await Create(root).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeFalse();
		reading.Problem.Should().Contain("no pt.* key");
	}

	/// <summary>
	/// Verifies that a table with an unsupported block compression is a failure that says why.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_UnsupportedBlockCompression_ShouldFail()
	{
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "000005.ldb"), LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.card"), 1, LevelDbBuilder.Value("{}"))], block => block, 2));

		var reading = await Create(root).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeFalse();
		reading.Problem.Should().Contain("unsupported compression");
	}

	/// <summary>
	/// Verifies that values of a table and of a newer log are merged, and that a lock file next to them is ignored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_LogAndTable_ShouldMergeTheValues()
	{
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "000005.ldb"), LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.card"), 1, LevelDbBuilder.Value("""{"sector":"louise"}"""))]));
		await File.WriteAllBytesAsync(Path.Combine(root, "000007.log"), LevelDbBuilder.Log((2, [(LevelDbBuilder.Key("pt.layers"), LevelDbBuilder.Value("""{"works":false}"""))])));
		await File.WriteAllBytesAsync(Path.Combine(root, "LOCK"), []);

		var reading = await Create(root).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
		reading.Reader.Should().Be("leveldb");
		reading.Values.Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.card"] = """{"sector":"louise"}""", ["pt.layers"] = """{"works":false}""" });
	}

	/// <summary>
	/// Verifies that a corrupt table is a failed read with a reason, not an empty success.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_CorruptTable_ShouldFail()
	{
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "000005.ldb"), new byte[200]);

		var reading = await Create(root).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeFalse();
		reading.Problem.Should().Contain("InvalidDataException");
	}

	/// <summary>
	/// Verifies that an empty directory path is rejected.
	/// </summary>
	[Fact]
	public void Constructor_EmptyDirectory_ShouldThrow()
	{
		var create = () => Create(" ");

		create.Should().Throw<ArgumentException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates the reader over a directory with a logger that discards its messages.
	/// </summary>
	/// <param name="directory">The directory.</param>
	/// <returns>The reader.</returns>
	private static LegacyLocalStorageReader Create(string directory)
	{
		return new LegacyLocalStorageReader(directory, NullLogger<LegacyLocalStorageReader>.Instance);
	}
	#endregion
}
