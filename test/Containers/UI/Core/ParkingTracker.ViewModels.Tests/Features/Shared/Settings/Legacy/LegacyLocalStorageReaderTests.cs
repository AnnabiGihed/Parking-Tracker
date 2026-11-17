using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="LegacyLocalStorageReader"/> against a temporary directory laid out like the web view's
///              <c>Local Storage/leveldb</c>: a missing directory is a new install, a log and a table are both read, and a corrupt
///              table is a failure (so that the migration tries again) and not an empty result.
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
		var reading = await new LegacyLocalStorageReader(Path.Combine(root, "none")).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
		reading.Values.Should().BeEmpty();
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

		var reading = await new LegacyLocalStorageReader(root).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
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

		var reading = await new LegacyLocalStorageReader(root).ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeFalse();
		reading.Problem.Should().Contain("InvalidDataException");
	}

	/// <summary>
	/// Verifies that an empty directory path is rejected.
	/// </summary>
	[Fact]
	public void Constructor_EmptyDirectory_ShouldThrow()
	{
		var create = () => new LegacyLocalStorageReader(" ");

		create.Should().Throw<ArgumentException>();
	}
	#endregion

	#region Reading Tests
	/// <summary>
	/// Verifies the two kinds of answer of <see cref="LegacyStorageReading"/>.
	/// </summary>
	[Fact]
	public void LegacyStorageReading_SuccessAndFailure_ShouldCarryTheirData()
	{
		var success = LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.card"] = "{}" });
		var failure = LegacyStorageReading.Failure("disk");

		success.Succeeded.Should().BeTrue();
		success.Values.Should().ContainKey("pt.card");
		failure.Succeeded.Should().BeFalse();
		failure.Problem.Should().Be("disk");
		failure.Values.Should().BeEmpty();
		var empty = () => LegacyStorageReading.Failure(" ");
		empty.Should().Throw<ArgumentException>();
	}
	#endregion
}
