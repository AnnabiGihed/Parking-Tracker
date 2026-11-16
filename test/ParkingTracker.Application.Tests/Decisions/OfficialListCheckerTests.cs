using NSubstitute;
using ParkingTracker.Application.Decisions;
using ParkingTracker.Application.Sources;
using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="OfficialListChecker"/>: unchanged documents proven by headers or by content, a changed
///              document, a document that is not a PDF, a failed download and how long an outcome is kept.
/// </summary>
public sealed class OfficialListCheckerTests
{
	#region Test Infrastructure
	private const string Url = "https://example.test/sector.pdf";
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(1));

	private static SectorModel Sector(string id = "louise")
	{
		var source = new SourceFingerprint(Url, "\"abc\"", 100, "Mon, 09 Feb 2026 10:32:48 GMT", "ff00");
		return SectorModel.Build(new SectorDefinition(id, "Louise", "Louiza", "BRUXELLES", null, [source], [], [])).Value;
	}

	private static ICityDocumentsClient Documents(DocumentHeaders? headers, DownloadedDocument? download = null)
	{
		var client = Substitute.For<ICityDocumentsClient>();
		client.GetHeadersAsync(Url, Arg.Any<CancellationToken>()).Returns(headers is null
			? Result.Failure<DocumentHeaders>(new Error("City.Unavailable", "bruxelles.be timed out"))
			: Result.Success(headers));
		client.DownloadAsync(Url, Arg.Any<CancellationToken>()).Returns(download is null
			? Result.Failure<DownloadedDocument>(new Error("City.Unavailable", "bruxelles.be HTTP 404"))
			: Result.Success(download));
		return client;
	}
	#endregion

	#region Check Tests
	/// <summary>
	/// Verifies that matching headers prove the document unchanged without downloading it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_HeadersMatch_ShouldBeUnchangedWithoutDownloading()
	{
		var client = Documents(new DocumentHeaders(200, "\"abc\"", null));
		using var checker = new OfficialListChecker(client);

		var status = await checker.EnsureFreshAsync(Sector(), Now, CancellationToken.None);

		status.State.Should().Be(OfficialListState.Ok);
		await client.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that when the headers differ the content decides: the same hash is still unchanged.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_HeadersDifferButContentSame_ShouldBeUnchanged()
	{
		using var checker = new OfficialListChecker(Documents(new DocumentHeaders(200, "\"zzz\"", null), new DownloadedDocument(true, "ff00")));

		var status = await checker.EnsureFreshAsync(Sector(), Now, CancellationToken.None);

		status.State.Should().Be(OfficialListState.Ok);
	}

	/// <summary>
	/// Verifies that a different content is reported as changed with the address and the date the City sent.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_ContentDiffers_ShouldBeChangedWithTheDocumentAndDate()
	{
		var headers = new DocumentHeaders(200, "\"zzz\"", "Mon, 02 Nov 2026 09:00:00 GMT");
		using var checker = new OfficialListChecker(Documents(headers, new DownloadedDocument(true, "ee11")));

		var status = await checker.EnsureFreshAsync(Sector(), Now, CancellationToken.None);

		status.State.Should().Be(OfficialListState.Changed);
		status.Document.Should().Be(Url);
		status.DocumentDate.Should().Be("Mon, 02 Nov 2026 09:00:00 GMT");
	}

	/// <summary>
	/// Verifies that something that is not a PDF is not taken as a change, but as an unverified list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_NotAPdf_ShouldBeUnverified()
	{
		using var checker = new OfficialListChecker(Documents(null, new DownloadedDocument(false, "ee11")));

		var status = await checker.EnsureFreshAsync(Sector(), Now, CancellationToken.None);

		status.State.Should().Be(OfficialListState.Unverified);
		status.Why.Should().Be("the City sent something that is not the PDF");
	}

	/// <summary>
	/// Verifies that a failed download is an unverified list with the reason, and a failed header request alone is not an error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_DownloadFails_ShouldBeUnverifiedWithTheReason()
	{
		using var checker = new OfficialListChecker(Documents(null));

		var status = await checker.EnsureFreshAsync(Sector(), Now, CancellationToken.None);

		status.State.Should().Be(OfficialListState.Unverified);
		status.Why.Should().Be("bruxelles.be HTTP 404");
	}
	#endregion

	#region Caching Tests
	/// <summary>
	/// Verifies that an unchanged outcome is reused for 6 hours.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_UnchangedWithinSixHours_ShouldNotCheckAgain()
	{
		var client = Documents(new DocumentHeaders(200, "\"abc\"", null));
		using var checker = new OfficialListChecker(client);
		await checker.EnsureFreshAsync(Sector(), Now, CancellationToken.None);

		await checker.EnsureFreshAsync(Sector(), Now.AddHours(5), CancellationToken.None);
		await checker.EnsureFreshAsync(Sector(), Now.AddHours(7), CancellationToken.None);

		await client.Received(2).GetHeadersAsync(Url, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that an outcome that is not "unchanged" is retried after a minute.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_UnverifiedAfterAMinute_ShouldCheckAgain()
	{
		var client = Documents(null);
		using var checker = new OfficialListChecker(client);
		await checker.EnsureFreshAsync(Sector(), Now, CancellationToken.None);

		await checker.EnsureFreshAsync(Sector(), Now.AddSeconds(30), CancellationToken.None);
		await checker.EnsureFreshAsync(Sector(), Now.AddSeconds(90), CancellationToken.None);

		await client.Received(2).GetHeadersAsync(Url, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that another sector is checked on its own, not answered from the outcome of the previous one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EnsureFreshAsync_AnotherSector_ShouldCheckAgain()
	{
		var client = Documents(new DocumentHeaders(200, "\"abc\"", null));
		using var checker = new OfficialListChecker(client);

		await checker.EnsureFreshAsync(Sector("louise"), Now, CancellationToken.None);
		await checker.EnsureFreshAsync(Sector("schuman"), Now, CancellationToken.None);

		await client.Received(2).GetHeadersAsync(Url, Arg.Any<CancellationToken>());
	}
	#endregion
}
