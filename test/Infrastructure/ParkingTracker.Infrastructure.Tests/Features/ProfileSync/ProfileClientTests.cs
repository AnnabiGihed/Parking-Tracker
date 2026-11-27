using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Infrastructure.Features.ProfileSync;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of <see cref="ProfileClient"/> against a stub of the API (ADR-0032): a read sends <c>If-None-Match</c> with the revision in quotes
///              and reads 304, 404 and 200 with its <c>ETag</c> and document; a write sends <c>If-Match</c> with the revision or <c>If-None-Match: *</c> to create
///              and reads the new revision, 412 as stale and 400, 413 and 428 as refusals; a missing session, a network failure, a timeout, a server
///              fault and an unreadable body are failed results with their own codes, and the document is written in the JSON of the API.
/// </summary>
public sealed class ProfileClientTests
{
	#region Constants
	/// <summary>
	/// A document as the API answers it.
	/// </summary>
	private const string DocumentJson = """{"schemaVersion":1,"home":{"label":"Home","latitude":50.85,"longitude":4.35,"addressText":"Rue","updatedAt":"2026-11-02T09:00:00+00:00"},"work":null,"favourites":{"updatedAt":"2026-11-02T09:00:00+00:00","places":[{"label":"Gym","latitude":50.84,"longitude":4.36,"addressText":null}]},"vehicle":null,"card":{"municipality":"bruxelles","sector":"louise","validFrom":"2026-01-01","validTo":null,"tolerances":["T1"],"mailles":[],"lims":[],"number":"123","plate":"1ABC234","updatedAt":"2026-11-02T09:00:00+00:00"}}""";
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// A document to write.
	/// </summary>
	private static readonly ProfileDocument Document = new(
		ProfileDocument.SupportedSchemaVersion,
		new ProfilePlaceSection("Home", 50.85, 4.35, "Rue", new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)),
		null,
		new ProfileFavouritesSection(DateTimeOffset.UnixEpoch, []),
		null,
		null);

	/// <summary>
	/// Builds the client over a stub of the API.
	/// </summary>
	/// <param name="respond">Answers a request.</param>
	/// <param name="handler">The stub, so that a test can read the last request.</param>
	/// <returns>The client.</returns>
	private static ProfileClient Create(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHttpMessageHandler handler)
	{
		handler = new StubHttpMessageHandler((request, _) => Task.FromResult(respond(request)));
		return new ProfileClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") });
	}

	/// <summary>
	/// Builds a response with a status and an entity tag.
	/// </summary>
	/// <param name="status">The status.</param>
	/// <param name="tag">The entity tag, or <see langword="null"/>.</param>
	/// <param name="body">The body.</param>
	/// <returns>The response.</returns>
	private static HttpResponseMessage Answer(HttpStatusCode status, string? tag = null, string body = "")
	{
		var response = StubHttpMessageHandler.Text(status, body);
		if (tag is not null)
		{
			response.Headers.ETag = new EntityTagHeaderValue(tag);
		}

		return response;
	}
	#endregion

	#region Read Tests
	/// <summary>
	/// Verifies that a read sends the revision in quotes and that 304 means the copy of the phone is current.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_NotModified_ShouldSendTheRevisionAndReadNotModified()
	{
		var client = Create(_ => Answer(HttpStatusCode.NotModified), out var handler);

		var result = await client.GetAsync(3);

		result.Value.Outcome.Should().Be(ProfileGetOutcome.NotModified);
		handler.LastHeaders!.IfNoneMatch.Select(tag => tag.Tag).Should().Equal("\"3\"");
		handler.Requests.Should().Equal("GET https://api.example.test/api/v1/me/profile");
	}

	/// <summary>
	/// Verifies that a read without a revision sends no condition.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_NoRevision_ShouldSendNoCondition()
	{
		var client = Create(_ => Answer(HttpStatusCode.NotFound), out var handler);

		var result = await client.GetAsync(null);

		result.Value.Outcome.Should().Be(ProfileGetOutcome.NotFound);
		handler.LastHeaders!.IfNoneMatch.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a 200 gives the document with every section and the revision of the <c>ETag</c>.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_Found_ShouldReadTheDocumentAndTheRevision()
	{
		var client = Create(_ => Answer(HttpStatusCode.OK, "\"5\"", DocumentJson), out _);

		var result = await client.GetAsync(null);

		result.Value.Outcome.Should().Be(ProfileGetOutcome.Found);
		result.Value.Revision.Should().Be(5);
		var document = result.Value.Document!;
		document.Home!.Label.Should().Be("Home");
		document.Home.UpdatedAt.Should().Be(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
		document.Work.Should().BeNull();
		document.Favourites.Places.Should().ContainSingle().Which.Label.Should().Be("Gym");
		document.Card!.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
		document.Card.Tolerances.Should().Equal("T1");
		document.Card.Number.Should().Be("123");
	}

	/// <summary>
	/// Verifies that a 200 without a usable <c>ETag</c>, or with a body that is not the document, is a server failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_NoEntityTagOrBadBody_ShouldFailAsServer()
	{
		var noTag = Create(_ => Answer(HttpStatusCode.OK, null, DocumentJson), out _);
		var weirdTag = Create(_ => Answer(HttpStatusCode.OK, "\"abc\"", DocumentJson), out _);
		var badBody = Create(_ => Answer(HttpStatusCode.OK, "\"5\"", "{not json"), out _);

		(await noTag.GetAsync(null)).Error.Code.Should().Be(AccountErrors.ServerCode);
		(await weirdTag.GetAsync(null)).Error.Code.Should().Be(AccountErrors.ServerCode);
		(await badBody.GetAsync(null)).Error.Code.Should().Be(AccountErrors.ServerCode);
	}

	/// <summary>
	/// Verifies that a 401 is "not signed in" and that any other status is a server failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_UnauthorizedOrFault_ShouldFailWithTheMatchingCode()
	{
		var unauthorized = Create(_ => Answer(HttpStatusCode.Unauthorized), out _);
		var fault = Create(_ => Answer(HttpStatusCode.InternalServerError), out _);
		var limited = Create(_ => Answer((HttpStatusCode)429), out _);

		(await unauthorized.GetAsync(null)).Error.Code.Should().Be(AccountErrors.NotSignedInCode);
		(await fault.GetAsync(null)).Error.Code.Should().Be(AccountErrors.ServerCode);
		(await limited.GetAsync(null)).Error.Code.Should().Be(AccountErrors.ServerCode);
	}

	/// <summary>
	/// Verifies that a network failure and a timeout are network failures, and that a cancel by the caller is not swallowed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAsync_NetworkFailureOrTimeout_ShouldFailAsNetwork()
	{
		var down = new ProfileClient(new HttpClient(new StubHttpMessageHandler((_, _) => throw new HttpRequestException("down"))) { BaseAddress = new Uri("https://api.example.test/") });
		var timedOut = new ProfileClient(new HttpClient(new StubHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout"))) { BaseAddress = new Uri("https://api.example.test/") });
		using var source = new CancellationTokenSource();
		await source.CancelAsync();

		(await down.GetAsync(null)).Error.Code.Should().Be(AccountErrors.NetworkCode);
		(await timedOut.GetAsync(null)).Error.Code.Should().Be(AccountErrors.NetworkCode);
		var cancelled = () => timedOut.GetAsync(null, source.Token);
		await cancelled.Should().ThrowAsync<OperationCanceledException>();
	}
	#endregion

	#region Write Tests
	/// <summary>
	/// Verifies that a write that replaces a revision sends <c>If-Match</c> with it, the document in the JSON of the API, and reads the new revision.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PutAsync_Replace_ShouldSendIfMatchAndTheDocumentAndReadTheRevision()
	{
		string? body = null;
		var client = Create(request =>
		{
			body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
			return Answer(HttpStatusCode.OK, "\"6\"", """{"revision":6}""");
		}, out var handler);

		var result = await client.PutAsync(Document, 5);

		result.Value.Outcome.Should().Be(ProfilePutOutcome.Saved);
		result.Value.Revision.Should().Be(6);
		handler.LastHeaders!.IfMatch.Select(tag => tag.Tag).Should().Equal("\"5\"");
		handler.LastHeaders.IfNoneMatch.Should().BeEmpty();
		handler.Requests.Should().Equal("PUT https://api.example.test/api/v1/me/profile");
		using var json = JsonDocument.Parse(body!);
		json.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
		json.RootElement.GetProperty("home").GetProperty("label").GetString().Should().Be("Home");
		json.RootElement.GetProperty("favourites").GetProperty("places").GetArrayLength().Should().Be(0);
	}

	/// <summary>
	/// Verifies that a first write sends <c>If-None-Match: *</c> and no <c>If-Match</c>.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PutAsync_Create_ShouldSendIfNoneMatchStar()
	{
		var client = Create(_ => Answer(HttpStatusCode.OK, "\"1\"", """{"revision":1}"""), out var handler);

		var result = await client.PutAsync(Document, null);

		result.Value.Revision.Should().Be(1);
		handler.LastHeaders!.IfNoneMatch.Should().ContainSingle().Which.Should().Be(EntityTagHeaderValue.Any);
		handler.LastHeaders.IfMatch.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that 412 is stale and that 400, 413 and 428 are refusals that carry their status.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PutAsync_StaleAndRefusals_ShouldBeOutcomes()
	{
		var stale = Create(_ => Answer(HttpStatusCode.PreconditionFailed), out _);

		(await stale.PutAsync(Document, 5)).Value.Outcome.Should().Be(ProfilePutOutcome.Stale);
		foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.PreconditionRequired })
		{
			var refused = Create(_ => Answer(status), out _);

			var result = await refused.PutAsync(Document, 5);

			result.Value.Outcome.Should().Be(ProfilePutOutcome.Rejected);
			result.Value.Status.Should().Be((int)status);
		}
	}

	/// <summary>
	/// Verifies that failures of a write carry the same codes as the failures of a read, and that a body without a revision is a server failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PutAsync_Failures_ShouldCarryTheMatchingCode()
	{
		var unauthorized = Create(_ => Answer(HttpStatusCode.Unauthorized), out _);
		var fault = Create(_ => Answer(HttpStatusCode.BadGateway), out _);
		var noBody = Create(_ => Answer(HttpStatusCode.OK, "\"6\"", "null"), out _);
		var down = new ProfileClient(new HttpClient(new StubHttpMessageHandler((_, _) => throw new HttpRequestException("down"))) { BaseAddress = new Uri("https://api.example.test/") });

		(await unauthorized.PutAsync(Document, 5)).Error.Code.Should().Be(AccountErrors.NotSignedInCode);
		(await fault.PutAsync(Document, 5)).Error.Code.Should().Be(AccountErrors.ServerCode);
		(await noBody.PutAsync(Document, 5)).Error.Code.Should().Be(AccountErrors.ServerCode);
		(await down.PutAsync(Document, 5)).Error.Code.Should().Be(AccountErrors.NetworkCode);
	}

	/// <summary>
	/// Verifies that a missing argument is rejected and that the client needs an HTTP client.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task NullArguments_ShouldThrowArgumentNullException()
	{
		var client = Create(_ => Answer(HttpStatusCode.OK), out _);

		var noHttp = () => new ProfileClient(null!); // null on purpose: proves the guard clause rejects it
		var noDocument = () => client.PutAsync(null!, 1); // null on purpose: proves the guard clause rejects it

		noHttp.Should().Throw<ArgumentNullException>();
		await noDocument.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion
}
