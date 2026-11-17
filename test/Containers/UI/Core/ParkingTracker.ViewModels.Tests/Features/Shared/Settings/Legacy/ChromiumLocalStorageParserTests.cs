using System.Text;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy.LevelDb;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ChromiumLocalStorageParser"/> on byte layouts of a LevelDB log and table as the old web view
///              wrote them: Latin-1 and UTF-16 values, the origin filter, the newest write winning across files, deletions, split
///              records, Snappy blocks and the failures that must not be hidden (unsupported compression, truncated table).
/// </summary>
public sealed class ChromiumLocalStorageParserTests
{
	#region Constants
	/// <summary>
	/// A card as the old app wrote it.
	/// </summary>
	private const string CardJson = """{"number":"TEST","plate":"1TST000","sector":"louise","from":"2026-01-01","to":"2099-12-31","tolerances":[]}""";

	/// <summary>
	/// The origin of the old app.
	/// </summary>
	private const string Origin = "https://localhost";
	#endregion

	#region Log Tests
	/// <summary>
	/// Verifies that a Latin-1 value in a log is read under its key name.
	/// </summary>
	[Fact]
	public void Read_LatinValueInLog_ShouldGiveTheText()
	{
		var log = LevelDbBuilder.Log((1, [(LevelDbBuilder.Key("pt.card"), LevelDbBuilder.Value(CardJson))]));

		var values = Parse(new LevelDbFile("000003.log", log));

		values.Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.card"] = CardJson });
	}

	/// <summary>
	/// Verifies that a value with a character outside Latin-1 is stored as UTF-16 and read back unchanged.
	/// </summary>
	[Fact]
	public void Read_Utf16Value_ShouldGiveTheText()
	{
		const string Car = """{"lat":50.8,"lon":4.3,"t":1,"status":"Parked ✓","where":"Rue de l'Étoile"}""";
		var log = LevelDbBuilder.Log((1, [(LevelDbBuilder.Key("pt.car"), LevelDbBuilder.Value(Car))]));

		Parse(new LevelDbFile("000003.log", log))["pt.car"].Should().Be(Car);
		LevelDbBuilder.Value(Car)[0].Should().Be(0, "a character outside Latin-1 makes Chromium store UTF-16");
	}

	/// <summary>
	/// Verifies that other origins, other kinds of key and other prefixes are ignored.
	/// </summary>
	[Fact]
	public void Read_OtherOriginAndMetaKeys_ShouldBeIgnored()
	{
		var log = LevelDbBuilder.Log((1, [
			(LevelDbBuilder.Key("pt.card", "https://appassets.example"), LevelDbBuilder.Value("{}")),
			(LevelDbBuilder.Key("other"), LevelDbBuilder.Value("x")),
			(Encoding.UTF8.GetBytes("META:https://localhost"), [8, 1]),
			(Encoding.UTF8.GetBytes("VERSION"), Encoding.UTF8.GetBytes("1")),
			(LevelDbBuilder.Key("pt.layers"), LevelDbBuilder.Value("{}")),
		]));

		Parse(new LevelDbFile("000003.log", log)).Keys.Should().Equal("pt.layers");
	}

	/// <summary>
	/// Verifies that the later write of a key wins, and that a deletion removes it.
	/// </summary>
	[Fact]
	public void Read_OverwriteAndDeletion_ShouldKeepTheNewest()
	{
		var log = LevelDbBuilder.Log(
			(1, [(LevelDbBuilder.Key("pt.bgAlerts"), LevelDbBuilder.Value("true")), (LevelDbBuilder.Key("pt.card"), LevelDbBuilder.Value(CardJson))]),
			(3, [(LevelDbBuilder.Key("pt.bgAlerts"), LevelDbBuilder.Value("false")), (LevelDbBuilder.Key("pt.card"), null)]));

		var values = Parse(new LevelDbFile("000003.log", log));

		values.Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.bgAlerts"] = "false" });
	}

	/// <summary>
	/// Verifies that a batch split over blocks (first, middle and last records) is joined.
	/// </summary>
	[Fact]
	public void Read_BatchSplitOverBlocks_ShouldJoinTheRecords()
	{
		var big = LevelDbBuilder.Value(new string('x', 70000));
		var log = LevelDbBuilder.SplitLog(1, (LevelDbBuilder.Key("pt.big"), big), (LevelDbBuilder.Key("pt.card"), LevelDbBuilder.Value(CardJson)));

		var values = Parse(new LevelDbFile("000003.log", log));

		values["pt.big"].Should().HaveLength(70000);
		values["pt.card"].Should().Be(CardJson);
	}

	/// <summary>
	/// Verifies that a log that ends in a half-written record still gives the writes before it.
	/// </summary>
	[Fact]
	public void Read_LogCutInTheLastRecord_ShouldKeepTheEarlierWrites()
	{
		var log = LevelDbBuilder.Log(
			(1, [(LevelDbBuilder.Key("pt.card"), LevelDbBuilder.Value(CardJson))]),
			(2, [(LevelDbBuilder.Key("pt.layers"), LevelDbBuilder.Value("{}"))]));

		var values = Parse(new LevelDbFile("000003.log", log[..^5]));

		values.Keys.Should().Equal("pt.card");
	}

	/// <summary>
	/// Verifies that an empty log gives no values.
	/// </summary>
	[Fact]
	public void Read_EmptyLog_ShouldGiveNothing()
	{
		Parse(new LevelDbFile("000003.log", [])).Should().BeEmpty();
	}
	#endregion

	#region Table Tests
	/// <summary>
	/// Verifies that a stored (uncompressed) table block is read.
	/// </summary>
	[Fact]
	public void Read_StoredTable_ShouldGiveTheText()
	{
		var table = LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.card"), 5, LevelDbBuilder.Value(CardJson))]);

		Parse(new LevelDbFile("000005.ldb", table))["pt.card"].Should().Be(CardJson);
	}

	/// <summary>
	/// Verifies that a Snappy block with copy elements (repeats of earlier bytes) is decompressed, and that the block really was
	/// made smaller by them.
	/// </summary>
	[Fact]
	public void Read_SnappyBlockWithCopies_ShouldGiveTheText()
	{
		const string Layers = """{"streets":true,"spaces":true,"works":true,"carparks":true,"chargers":true}""";
		var sizes = (Plain: 0, Packed: 0);
		var table = LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.layers"), 5, LevelDbBuilder.Value(Layers))], block =>
		{
			var packed = LevelDbBuilder.SnappyCompress(block);
			sizes = (block.Length, packed.Length);
			return packed;
		});

		Parse(new LevelDbFile("000005.ldb", table))["pt.layers"].Should().Be(Layers);
		sizes.Packed.Should().BeLessThan(sizes.Plain);
	}

	/// <summary>
	/// Verifies that a Snappy block with a long literal (length in extra bytes) is decompressed.
	/// </summary>
	[Fact]
	public void Read_SnappyBlockWithLongLiteral_ShouldGiveTheText()
	{
		var table = LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.card"), 5, LevelDbBuilder.Value(CardJson))], LevelDbBuilder.SnappyCompress);

		Parse(new LevelDbFile("000005.ldb", table))["pt.card"].Should().Be(CardJson);
	}

	/// <summary>
	/// Verifies that the log, which is newer, wins over a table with an older write of the same key, whatever the file order.
	/// </summary>
	[Fact]
	public void Read_LogNewerThanTable_ShouldPreferTheLog()
	{
		var table = LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.bgAlerts"), 5, LevelDbBuilder.Value("true")), (LevelDbBuilder.Key("pt.card"), 6, LevelDbBuilder.Value(CardJson))]);
		var log = LevelDbBuilder.Log((7, [(LevelDbBuilder.Key("pt.bgAlerts"), LevelDbBuilder.Value("false"))]));

		var values = Parse(new LevelDbFile("000007.log", log), new LevelDbFile("000005.ldb", table));

		values["pt.bgAlerts"].Should().Be("false");
		values["pt.card"].Should().Be(CardJson);
	}

	/// <summary>
	/// Verifies that a deletion in the table removes the key.
	/// </summary>
	[Fact]
	public void Read_DeletionInTable_ShouldRemoveTheKey()
	{
		var table = LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.card"), 6, null), (LevelDbBuilder.Key("pt.layers"), 4, LevelDbBuilder.Value("{}"))]);
		var log = LevelDbBuilder.Log((1, [(LevelDbBuilder.Key("pt.card"), LevelDbBuilder.Value(CardJson))]));

		Parse(new LevelDbFile("000005.ldb", table), new LevelDbFile("000003.log", log)).Keys.Should().Equal("pt.layers");
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a block with a compression type this reader does not know fails instead of giving partial data.
	/// </summary>
	[Fact]
	public void Read_UnsupportedCompression_ShouldThrow()
	{
		var table = LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.card"), 5, LevelDbBuilder.Value(CardJson))], block => block, 2);

		var read = () => Parse(new LevelDbFile("000005.ldb", table));

		read.Should().Throw<InvalidDataException>().WithMessage("*unsupported compression type 2*");
	}

	/// <summary>
	/// Verifies that a file that is not a table fails.
	/// </summary>
	[Fact]
	public void Read_FileWithoutTableMagic_ShouldThrow()
	{
		var read = () => Parse(new LevelDbFile("000005.ldb", new byte[100]));

		read.Should().Throw<InvalidDataException>();
	}

	/// <summary>
	/// Verifies that a table cut short fails.
	/// </summary>
	[Fact]
	public void Read_TruncatedTable_ShouldThrow()
	{
		var table = LevelDbBuilder.Table([(LevelDbBuilder.Key("pt.card"), 5, LevelDbBuilder.Value(CardJson))]);

		var read = () => Parse(new LevelDbFile("000005.ldb", table[10..]));

		read.Should().Throw<InvalidDataException>();
	}

	/// <summary>
	/// Verifies that a Snappy stream that copies from before its start fails.
	/// </summary>
	[Fact]
	public void Read_SnappyCopyOutsideOutput_ShouldThrow()
	{
		var table = LevelDbBuilder.Table(
			[(LevelDbBuilder.Key("pt.card"), 5, LevelDbBuilder.Value(CardJson))],
			_ => [10, 1 | (0 << 2), 9]);

		var read = () => Parse(new LevelDbFile("000005.ldb", table));

		read.Should().Throw<InvalidDataException>();
	}

	/// <summary>
	/// Verifies that files that are neither a log nor a table (<c>LOCK</c>, <c>CURRENT</c>, <c>MANIFEST</c>) are ignored.
	/// </summary>
	[Fact]
	public void Read_OtherFiles_ShouldBeIgnored()
	{
		Parse(new LevelDbFile("LOCK", []), new LevelDbFile("CURRENT", Encoding.ASCII.GetBytes("MANIFEST-000001\n")), new LevelDbFile("MANIFEST-000001", [1, 2, 3])).Should().BeEmpty();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Parses the files for the origin of the old app and the key prefix <c>pt.</c>.
	/// </summary>
	/// <param name="files">The files.</param>
	/// <returns>The values by key.</returns>
	private static IReadOnlyDictionary<string, string> Parse(params LevelDbFile[] files) => ChromiumLocalStorageParser.Read(files, Origin, "pt.");
	#endregion
}
