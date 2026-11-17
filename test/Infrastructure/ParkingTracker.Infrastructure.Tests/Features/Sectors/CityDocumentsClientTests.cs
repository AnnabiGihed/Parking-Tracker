using System.Net;
using System.Security.Cryptography;
using System.Text;
using ParkingTracker.Application.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Infrastructure.Features.Sectors;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Tests.Features.Sectors.TestDoubles;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.Sectors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for the City documents client over a stub HTTP handler, alone (HEAD headers, download fingerprint,
///              failures) and together with the official-list checker (same date, same content under other headers, changed
///              content, not a PDF, a failed HEAD): headers are only a hint, only different content means changed.
/// </summary>
public sealed class CityDocumentsClientTests
{
	#region Constants
	/// <summary>
	/// The address of the document.
	/// </summary>
	private const string Url = "https://www.bruxelles.be/sites/default/files/bxl/TEST.pdf";

	/// <summary>
	/// The last modification date the tests give the document.
	/// </summary>
	private const string KnownDate = "Tue, 03 Mar 2026 10:00:00 GMT";
	#endregion

	#region Static Instances
	/// <summary>
	/// The moment the tests run at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// The bytes of the official list.
	/// </summary>
	private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 the official list");

	/// <summary>
	/// The bytes of a changed official list.
	/// </summary>
	private static readonly byte[] OtherPdf = Encoding.ASCII.GetBytes("%PDF-1.7 a changed list");
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Creates the client under test on top of the stub handler.
	/// </summary>
	/// <param name="handler">Stub handler that answers the requests.</param>
	/// <param name="timeouts">Time limits applied to each request.</param>
	/// <returns>City documents client.</returns>
	private static CityDocumentsClient ClientFor(StubHttpMessageHandler handler, SourceTimeouts? timeouts = null) => new(new HttpClient(handler), timeouts ?? SourceTimeouts.Default);

	/// <summary>
	/// Computes the lower-case hexadecimal SHA-256 of the content.
	/// </summary>
	/// <param name="bytes">Document content.</param>
	/// <returns>Hash text.</returns>
	private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

	/// <summary>
	/// Creates the response to a header check.
	/// </summary>
	/// <param name="status">HTTP status of the response.</param>
	/// <param name="etag">ETag header value, if any.</param>
	/// <param name="lastModified">Last-Modified header value, if any.</param>
	/// <returns>HTTP response.</returns>
	private static HttpResponseMessage Head(HttpStatusCode status, string? etag, string? lastModified)
	{
		var response = new HttpResponseMessage(status);
		if (etag is not null)
		{
			response.Headers.TryAddWithoutValidation("ETag", etag);
		}

		if (lastModified is not null)
		{
			response.Content.Headers.TryAddWithoutValidation("Last-Modified", lastModified);
		}

		return response;
	}

	/// <summary>
	/// Creates a successful response carrying the content.
	/// </summary>
	/// <param name="bytes">Document content.</param>
	/// <returns>HTTP response.</returns>
	private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

	/// <summary>
	/// Creates a sector whose saved fingerprint matches the content.
	/// </summary>
	/// <param name="verified">Content the saved fingerprint was verified against.</param>
	/// <returns>Sector model.</returns>
	private static SectorModel SectorVerifiedAgainst(byte[] verified)
	{
		var source = new SourceFingerprint(Url, "\"known\"", verified.Length, KnownDate, Sha(verified));
		var definition = new SectorDefinition("test", "Test", "Test", null, null, [source], [], []);
		return SectorModel.Build(definition).Value;
	}

	/// <summary>
	/// Creates a handler that answers HEAD and GET requests like the city server.
	/// </summary>
	/// <param name="head">Response to the HEAD request.</param>
	/// <param name="get">Factory of the response to the GET request.</param>
	/// <returns>Stub handler.</returns>
	private static StubHttpMessageHandler City(HttpResponseMessage head, Func<HttpResponseMessage> get)
	{
		return new StubHttpMessageHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Head ? head : get()));
	}

	/// <summary>
	/// Runs the official-list check against the handler.
	/// </summary>
	/// <param name="handler">Stub handler that answers the requests.</param>
	/// <returns>The resulting status and the handler.</returns>
	private static async Task<(OfficialListStatus Status, StubHttpMessageHandler Handler)> CheckAsync(StubHttpMessageHandler handler)
	{
		using var checker = new OfficialListChecker(ClientFor(handler));
		var status = await checker.EnsureFreshAsync(SectorVerifiedAgainst(Pdf), Now, CancellationToken.None).ConfigureAwait(false);
		return (status, handler);
	}
	#endregion

	#region Client Tests
	/// <summary>
	/// Verifies that the headers are read from a HEAD request, with the status and both header values as sent.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetHeaders_Answer_ShouldReturnStatusAndHeadersFromAHeadRequest()
	{
		var handler = City(Head(HttpStatusCode.OK, "W/\"abc\"", KnownDate), () => Bytes(Pdf));

		var result = await ClientFor(handler).GetHeadersAsync(Url, CancellationToken.None);

		result.Value.HttpStatus.Should().Be(200);
		result.Value.ETag.Should().Be("W/\"abc\"");
		result.Value.LastModified.Should().Be(KnownDate);
		handler.Requests.Should().Equal($"HEAD {Url}");
	}

	/// <summary>
	/// Verifies that an HTTP error status on HEAD is a result for the check to judge, not a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetHeaders_NotFound_ShouldReturnTheStatus()
	{
		var handler = City(Head(HttpStatusCode.NotFound, null, null), () => Bytes(Pdf));

		var result = await ClientFor(handler).GetHeadersAsync(Url, CancellationToken.None);

		result.Value.HttpStatus.Should().Be(404);
		result.Value.ETag.Should().BeNull();
		result.Value.LastModified.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a HEAD that does not answer in time is a Timeout failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetHeaders_NoAnswerInTime_ShouldFailWithTimeout()
	{
		var result = await ClientFor(StubHttpMessageHandler.Silent(), new SourceTimeouts(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50))).GetHeadersAsync(Url, CancellationToken.None);

		result.Error.Code.Should().Be("City.Timeout");
	}

	/// <summary>
	/// Verifies that a downloaded PDF is reported with its lower-case SHA-256.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Download_Pdf_ShouldReturnItsFingerprint()
	{
		var result = await ClientFor(City(Head(HttpStatusCode.OK, null, null), () => Bytes(Pdf))).DownloadAsync(Url, CancellationToken.None);

		result.Value.IsPdf.Should().BeTrue();
		result.Value.Sha256.Should().Be(Sha(Pdf));
	}

	/// <summary>
	/// Verifies that something that does not start with the PDF signature is reported as not a PDF.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Download_Html_ShouldReportNotAPdf()
	{
		var html = Encoding.ASCII.GetBytes("<html>Please accept cookies</html>");

		var result = await ClientFor(City(Head(HttpStatusCode.OK, null, null), () => Bytes(html))).DownloadAsync(Url, CancellationToken.None);

		result.Value.IsPdf.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that an HTTP error on the download is an Unavailable failure with the status.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Download_NotFound_ShouldFailWithTheStatus()
	{
		var result = await ClientFor(StubHttpMessageHandler.Always(HttpStatusCode.NotFound, string.Empty)).DownloadAsync(Url, CancellationToken.None);

		result.Error.Code.Should().Be("City.Unavailable");
		result.Error.Message.Should().Be("bruxelles.be HTTP 404");
	}

	/// <summary>
	/// Verifies that a connection dropped while the body is being read is an Unavailable failure, not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Download_BodyReadFails_ShouldFailWithUnavailable()
	{
		var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) }));

		var result = await ClientFor(handler).DownloadAsync(Url, CancellationToken.None);

		result.Error.Code.Should().Be("City.Unavailable");
		result.Error.Message.Should().Be("bruxelles.be unreachable");
	}

	/// <summary>
	/// Verifies that a network error raised because the limit of time fired is reported as a Timeout, not as Unavailable.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetHeaders_NetworkErrorAfterTheLimit_ShouldFailWithTimeout()
	{
		var handler = new StubHttpMessageHandler(async (_, token) =>
		{
			try
			{
				await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException exception)
			{
				throw new HttpRequestException("connection torn down", exception);
			}

			return new HttpResponseMessage(HttpStatusCode.OK);
		});

		var result = await ClientFor(handler, new SourceTimeouts(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50))).GetHeadersAsync(Url, CancellationToken.None);

		result.Error.Code.Should().Be("City.Timeout");
	}

	/// <summary>
	/// Verifies that the download has its own, longer limit: a download slower than a request limit still succeeds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Download_SlowerThanTheRequestLimit_ShouldUseTheDownloadLimit()
	{
		var handler = new StubHttpMessageHandler(async (_, token) =>
		{
			await Task.Delay(150, token).ConfigureAwait(false);
			return Bytes(Pdf);
		});

		var result = await ClientFor(handler, new SourceTimeouts(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(10))).DownloadAsync(Url, CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a download cancelled by the caller is rethrown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Download_Cancelled_ShouldThrowOperationCanceled()
	{
		using var source = new CancellationTokenSource();
		await source.CancelAsync();

		var act = () => ClientFor(StubHttpMessageHandler.Silent()).DownloadAsync(Url, source.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}
	#endregion

	#region Official List Flow Tests
	/// <summary>
	/// Verifies that the same date means unchanged without downloading the document.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_SameDate_ShouldBeOkWithoutDownload()
	{
		var (status, handler) = await CheckAsync(City(Head(HttpStatusCode.OK, null, KnownDate), () => Bytes(Pdf)));

		status.State.Should().Be(OfficialListState.Ok);
		handler.Requests.Should().Equal($"HEAD {Url}");
	}

	/// <summary>
	/// Verifies that different headers over the same content still mean unchanged.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_DifferentHeadersSameContent_ShouldBeOk()
	{
		var (status, handler) = await CheckAsync(City(Head(HttpStatusCode.OK, "\"other\"", "Wed, 04 Mar 2026 10:00:00 GMT"), () => Bytes(Pdf)));

		status.State.Should().Be(OfficialListState.Ok);
		handler.Requests.Should().Equal($"HEAD {Url}", $"GET {Url}");
	}

	/// <summary>
	/// Verifies that different content means changed, and that the date of the document is passed on.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_DifferentContent_ShouldBeChanged()
	{
		var (status, _) = await CheckAsync(City(Head(HttpStatusCode.OK, "\"other\"", "Wed, 04 Mar 2026 10:00:00 GMT"), () => Bytes(OtherPdf)));

		status.State.Should().Be(OfficialListState.Changed);
		status.Document.Should().Be(Url);
		status.DocumentDate.Should().Be("Wed, 04 Mar 2026 10:00:00 GMT");
	}

	/// <summary>
	/// Verifies that a page that is not a PDF is unverified, not changed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_NotAPdf_ShouldBeUnverified()
	{
		var (status, _) = await CheckAsync(City(Head(HttpStatusCode.OK, null, null), () => Bytes(Encoding.ASCII.GetBytes("<html>cookies</html>"))));

		status.State.Should().Be(OfficialListState.Unverified);
		status.Why.Should().Be(OfficialListStatus.NotAPdfReason);
	}

	/// <summary>
	/// Verifies that a failed HEAD does not stop the check: the document is downloaded and compared.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_HeadFails_ShouldFallBackToTheDownload()
	{
		var handler = new StubHttpMessageHandler((request, _) => request.Method == HttpMethod.Head
			? throw new HttpRequestException("reset")
			: Task.FromResult(Bytes(Pdf)));

		var (status, recorded) = await CheckAsync(handler);

		status.State.Should().Be(OfficialListState.Ok);
		recorded.Requests.Should().Equal($"HEAD {Url}", $"GET {Url}");
	}

	/// <summary>
	/// Verifies that a download that fails is unverified with the reason shown to the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_DownloadFails_ShouldBeUnverifiedWithTheReason()
	{
		var (status, _) = await CheckAsync(StubHttpMessageHandler.Always(HttpStatusCode.ServiceUnavailable, string.Empty));

		status.State.Should().Be(OfficialListState.Unverified);
		status.Why.Should().Be("bruxelles.be HTTP 503");
	}
	#endregion
}
