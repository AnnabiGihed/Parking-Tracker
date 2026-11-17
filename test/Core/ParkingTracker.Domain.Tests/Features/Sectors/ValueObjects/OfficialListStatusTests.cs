using ParkingTracker.Domain.Features.Sectors.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="OfficialListStatus"/>: the sentences for a changed or unchecked list, when a check is
///              repeated, and how a document is compared with the verified one.
/// </summary>
public sealed class OfficialListStatusTests
{
	#region Test Infrastructure
	/// <summary>
	/// The moment the tests run at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(1));

	/// <summary>
	/// The fingerprint of the official document the tests compare to.
	/// </summary>
	private static readonly SourceFingerprint Source = new("https://example.test/a.pdf", "\"abc\"", 100, "Mon, 09 Feb 2026 10:32:48 GMT", "ff00");
	#endregion

	#region Problem Tests
	/// <summary>
	/// Verifies that an unchanged list gives no problem.
	/// </summary>
	[Fact]
	public void Problem_Unchanged_ShouldBeNull()
	{
		OfficialListStatus.Unchanged(Now).Problem.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a changed document is reported with its date when known.
	/// </summary>
	[Fact]
	public void Problem_ChangedWithADate_ShouldNameTheDate()
	{
		var status = OfficialListStatus.ChangedDocument("https://example.test/a.pdf", "Mon, 02 Nov 2026 09:00:00 GMT", Now);

		status.Problem.Should().Be("The City of Brussels changed its official sector document on Mon, 02 Nov 2026 09:00:00 GMT. This app's street list may be outdated: update the app before relying on it.");
	}

	/// <summary>
	/// Verifies that a changed document without a date is reported without it.
	/// </summary>
	[Fact]
	public void Problem_ChangedWithoutADate_ShouldOmitTheDate()
	{
		var status = OfficialListStatus.ChangedDocument("https://example.test/a.pdf", null, Now);

		status.Problem.Should().StartWith("The City of Brussels changed its official sector document.");
	}

	/// <summary>
	/// Verifies that an unverified list gives the reason, or "not checked yet" without one.
	/// </summary>
	/// <param name="why">The reason, possibly empty.</param>
	/// <param name="expected">The expected reason in the sentence.</param>
	[Theory]
	[InlineData("bruxelles.be HTTP 404", "bruxelles.be HTTP 404")]
	[InlineData("", "not checked yet")]
	public void Problem_Unverified_ShouldGiveTheReason(string why, string expected)
	{
		OfficialListStatus.CouldNotCheck(why, Now).Problem.Should().Be($"Could not confirm that the City's official sector list is unchanged ({expected}).");
	}
	#endregion

	#region Staleness Tests
	/// <summary>
	/// Verifies when a check is repeated: never made, an unchanged result after 6 hours, any other result after 1 minute.
	/// </summary>
	/// <param name="state">The state of the last check.</param>
	/// <param name="ageMinutes">How old the last check is.</param>
	/// <param name="expected">Whether the check is repeated.</param>
	[Theory]
	[InlineData(OfficialListState.Ok, 359, false)]
	[InlineData(OfficialListState.Ok, 361, true)]
	[InlineData(OfficialListState.Unverified, 0.5, false)]
	[InlineData(OfficialListState.Unverified, 1.5, true)]
	[InlineData(OfficialListState.Changed, 2, true)]
	public void IsStale_ByStateAndAge_ShouldFollowTheIntervals(OfficialListState state, double ageMinutes, bool expected)
	{
		var status = new OfficialListStatus(state, Now - TimeSpan.FromMinutes(ageMinutes), null, null, null);

		OfficialListStatus.IsStale(status, Now).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that no status at all is stale.
	/// </summary>
	[Fact]
	public void IsStale_NoStatus_ShouldBeTrue()
	{
		OfficialListStatus.IsStale(null, Now).Should().BeTrue();
	}
	#endregion

	#region Header Comparison Tests
	/// <summary>
	/// Verifies that the headers prove a document unchanged by date or tag, ignoring tag decoration, and only for success statuses.
	/// </summary>
	/// <param name="status">The HTTP status.</param>
	/// <param name="eTag">The ETag header.</param>
	/// <param name="lastModified">The Last-Modified header.</param>
	/// <param name="expected">Whether the headers say "same".</param>
	[Theory]
	[InlineData(200, "\"abc\"", null, true)]
	[InlineData(200, "W/\"ABC\"", null, true)]
	[InlineData(200, null, "Mon, 09 Feb 2026 10:32:48 GMT", true)]
	[InlineData(200, "\"other\"", "Tue, 10 Feb 2026 10:32:48 GMT", false)]
	[InlineData(200, null, null, false)]
	[InlineData(404, "\"abc\"", "Mon, 09 Feb 2026 10:32:48 GMT", false)]
	public void HeadersSaySame_ByStatusAndHeaders_ShouldCompareDateAndTag(int status, string? eTag, string? lastModified, bool expected)
	{
		OfficialListStatus.HeadersSaySame(Source, status, eTag, lastModified).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that only identical content counts as the verified document.
	/// </summary>
	[Fact]
	public void IsVerifiedContent_SameAndDifferentHash_ShouldCompareExactly()
	{
		OfficialListStatus.IsVerifiedContent(Source, "ff00").Should().BeTrue();
		OfficialListStatus.IsVerifiedContent(Source, "ff01").Should().BeFalse();
	}
	#endregion
}
