using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Application.Tests.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="LegacyStorageReading"/>: the two kinds of answer carry their data (values and reader name, or
///              the reason of a failure), copy the values they are given, and reject missing input.
/// </summary>
public sealed class LegacyStorageReadingTests
{
	#region Success Tests
	/// <summary>
	/// Verifies that a success carries the values and the name of the reader.
	/// </summary>
	[Fact]
	public void Success_WithValues_ShouldCarryThemAndTheReader()
	{
		var reading = LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.card"] = "{}" }, LegacyStorageReading.WebViewReader);

		reading.Succeeded.Should().BeTrue();
		reading.Problem.Should().BeNull();
		reading.Reader.Should().Be("webview");
		reading.Values.Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.card"] = "{}" });
	}

	/// <summary>
	/// Verifies that a later change of the dictionary that was passed in does not change the reading.
	/// </summary>
	[Fact]
	public void Success_SourceChangedAfterwards_ShouldKeepItsOwnCopy()
	{
		var source = new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.car"] = "{}" };
		var reading = LegacyStorageReading.Success(source, LegacyStorageReading.LevelDbReader);

		source["pt.card"] = "{}";

		reading.Values.Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that missing values or an empty reader name are rejected.
	/// </summary>
	[Fact]
	public void Success_MissingInput_ShouldThrow()
	{
		var noValues = () => LegacyStorageReading.Success(null!, "leveldb"); // null on purpose: proves the guard clause rejects it
		var noReader = () => LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal), " ");

		noValues.Should().Throw<ArgumentNullException>();
		noReader.Should().Throw<ArgumentException>();
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a failure carries its reason, no values and the name <c>none</c>.
	/// </summary>
	[Fact]
	public void Failure_WithProblem_ShouldCarryTheReason()
	{
		var failure = LegacyStorageReading.Failure("disk");

		failure.Succeeded.Should().BeFalse();
		failure.Problem.Should().Be("disk");
		failure.Reader.Should().Be("none");
		failure.Values.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a failure without a reason is rejected.
	/// </summary>
	[Fact]
	public void Failure_EmptyProblem_ShouldThrow()
	{
		var empty = () => LegacyStorageReading.Failure(" ");

		empty.Should().Throw<ArgumentException>();
	}
	#endregion
}
