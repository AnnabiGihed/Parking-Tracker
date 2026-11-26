using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkingTracker.Api.E2E.Tests.Features.Authentication;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : End-to-end tests of <c>GET</c> and <c>PUT /api/v1/me/profile</c> (ADR-0032) on the real API composition and a real
///              PostGIS server: the access token, the 404 of an empty profile, the conditional write (create with
///              <c>If-None-Match: *</c>, replace with <c>If-Match</c>) with 412, 428 and 400 for the precondition, the strong
///              <c>ETag</c> and the 304, the round trip of the document, the validation, the limit of the body (413), the rate limit,
///              the isolation of accounts, the erasure, two creates at once, an unreadable stored profile, and the payload at rest.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class ProfileEndpointTests
{
	#region Constants
	/// <summary>
	/// The route of the profile endpoint.
	/// </summary>
	private const string ProfileRoute = "/api/v1/me/profile";

	/// <summary>
	/// The Google subject the tests sign in with.
	/// </summary>
	private const string Subject = "google-subject-profile";

	/// <summary>
	/// The Google subject of a second user.
	/// </summary>
	private const string OtherSubject = "google-subject-profile-other";

	/// <summary>
	/// An address that must never appear in the stored payload.
	/// </summary>
	private const string SecretAddress = "Rue Secrete 12, 1000 Bruxelles";

	/// <summary>
	/// The media type of a problem document.
	/// </summary>
	private const string ProblemMediaType = "application/problem+json";
	#endregion

	#region Static Instances
	/// <summary>
	/// The moment of the sections of the tests, a little in the past so the server accepts it at any clock.
	/// </summary>
	private static readonly DateTimeOffset Moment = DateTimeOffset.UtcNow.AddHours(-1);
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileEndpointTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public ProfileEndpointTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Access Tests
	/// <summary>
	/// Verifies that both endpoints answer 401 without an access token, and that a profile cannot be written then.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Profile_WithoutAccessToken_ShouldAnswer401()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var read = await SendAsync(harness, HttpMethod.Get, null);
		var write = await SendAsync(harness, HttpMethod.Put, null, Document(), CreateOnly);

		read.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		write.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		await using var context = harness.OpenContext();
		(await context.Set<AccountProfile>().CountAsync()).Should().Be(0);
	}

	/// <summary>
	/// Verifies that the profile of another account is not reachable: the other account has none, and its own write is untouched.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Profile_OfAnotherAccount_ShouldNotBeVisible()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var mine = await harness.SignInAsync(Subject);
		var other = await harness.SignInAsync(OtherSubject);
		_ = (await SendAsync(harness, HttpMethod.Put, mine.AccessToken, Document(), CreateOnly)).EnsureSuccessStatusCode();

		var read = await SendAsync(harness, HttpMethod.Get, other.AccessToken);
		var create = await SendAsync(harness, HttpMethod.Put, other.AccessToken, Document(), CreateOnly);

		read.StatusCode.Should().Be(HttpStatusCode.NotFound);
		create.StatusCode.Should().Be(HttpStatusCode.OK, "the other account has no profile yet, so its create is not refused");
		await using var context = harness.OpenContext();
		(await context.Set<AccountProfile>().CountAsync()).Should().Be(2);
	}
	#endregion

	#region Read Tests
	/// <summary>
	/// Verifies that an account without a profile gets 404 as a problem document.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_WithoutProfile_ShouldAnswer404()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);

		var response = await SendAsync(harness, HttpMethod.Get, device.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemMediaType);
		response.Headers.ETag.Should().BeNull();
	}

	/// <summary>
	/// Verifies that the document that was written is read back unchanged, with the revision as a strong quoted ETag and a
	/// cache header that keeps it private.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_AfterCreate_ShouldReturnTheSameDocumentWithTheETag()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		var document = Document();
		_ = (await SendAsync(harness, HttpMethod.Put, device.AccessToken, document, CreateOnly)).EnsureSuccessStatusCode();

		var response = await SendAsync(harness, HttpMethod.Get, device.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Headers.ETag!.Tag.Should().Be("\"1\"");
		response.Headers.ETag.IsWeak.Should().BeFalse();
		response.Headers.CacheControl!.Private.Should().BeTrue();
		response.Headers.CacheControl.NoCache.Should().BeTrue();
		(await response.Content.ReadFromJsonAsync<ProfileDocument>()).Should().BeEquivalentTo(document);
	}

	/// <summary>
	/// Verifies that an If-None-Match that names the current revision (also weak, in a list, or as *) answers 304 without a body
	/// and with the ETag, and that another revision gets the document.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_WithMatchingIfNoneMatch_ShouldAnswer304()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		_ = (await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), CreateOnly)).EnsureSuccessStatusCode();

		foreach (var header in new[] { "\"1\"", "W/\"1\"", "\"7\", \"1\"", "*" })
		{
			var response = await SendAsync(harness, HttpMethod.Get, device.AccessToken, configure: request => request.Headers.TryAddWithoutValidation("If-None-Match", header));

			response.StatusCode.Should().Be(HttpStatusCode.NotModified, "the header was {0}", header);
			(await response.Content.ReadAsStringAsync()).Should().BeEmpty();
			response.Headers.ETag!.Tag.Should().Be("\"1\"");
		}

		var stale = await SendAsync(harness, HttpMethod.Get, device.AccessToken, configure: request => request.Headers.TryAddWithoutValidation("If-None-Match", "\"2\""));
		stale.StatusCode.Should().Be(HttpStatusCode.OK);
	}
	#endregion

	#region Write Tests
	/// <summary>
	/// Verifies that a create answers 200 with the revision 1 in the body and in the ETag, and that a second create is 412 as a
	/// problem document and leaves the first profile as it was.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_CreateTwice_ShouldAnswer200ThenPrecondition412()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);

		var first = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), CreateOnly);
		var second = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("Other"), CreateOnly);

		first.StatusCode.Should().Be(HttpStatusCode.OK);
		first.Headers.ETag!.Tag.Should().Be("\"1\"");
		(await first.Content.ReadFromJsonAsync<ProfileRevisionResponse>())!.Revision.Should().Be(1);
		second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
		second.Content.Headers.ContentType?.MediaType.Should().Be(ProblemMediaType);
		var read = await SendAsync(harness, HttpMethod.Get, device.AccessToken);
		(await read.Content.ReadFromJsonAsync<ProfileDocument>())!.Home!.Label.Should().Be("Home");
	}

	/// <summary>
	/// Verifies that a stale If-Match is 412, the right one replaces the document and answers revision 2, and the revision
	/// continues to grow.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_Replace_ShouldCheckTheRevision()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		_ = (await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), CreateOnly)).EnsureSuccessStatusCode();

		var stale = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("Changed"), IfMatch("\"5\""));
		var current = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("Changed"), IfMatch("\"1\""));
		var again = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("Third"), IfMatch("\"1\""));
		var next = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("Third"), IfMatch("\"2\""));

		stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
		current.StatusCode.Should().Be(HttpStatusCode.OK);
		current.Headers.ETag!.Tag.Should().Be("\"2\"");
		again.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
		next.Headers.ETag!.Tag.Should().Be("\"3\"");
		var read = await SendAsync(harness, HttpMethod.Get, device.AccessToken);
		read.Headers.ETag!.Tag.Should().Be("\"3\"");
		(await read.Content.ReadFromJsonAsync<ProfileDocument>())!.Home!.Label.Should().Be("Third");
	}

	/// <summary>
	/// Verifies that a replacement without any profile stored is 412, and that no profile is created by it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_ReplaceWithoutProfile_ShouldAnswer412()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);

		var response = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), IfMatch("\"1\""));

		response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
		(await SendAsync(harness, HttpMethod.Get, device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	/// <summary>
	/// Verifies that a write without If-Match and If-None-Match is 428 as a problem document and stores nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_WithoutPrecondition_ShouldAnswer428()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);

		var response = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document());

		response.StatusCode.Should().Be((HttpStatusCode)428);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemMediaType);
		(await SendAsync(harness, HttpMethod.Get, device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	/// <summary>
	/// Verifies that a precondition that is malformed, weak, not a revision, or both headers together, is 400 and stores nothing.
	/// </summary>
	/// <param name="name">The header.</param>
	/// <param name="value">The value.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("If-Match", "1")]
	[InlineData("If-Match", "\"abc\"")]
	[InlineData("If-Match", "\"0\"")]
	[InlineData("If-Match", "\"-1\"")]
	[InlineData("If-Match", "\"01\"")]
	[InlineData("If-Match", "W/\"1\"")]
	[InlineData("If-Match", "\"1\", \"2\"")]
	[InlineData("If-Match", "*")]
	[InlineData("If-None-Match", "\"1\"")]
	[InlineData("If-None-Match", "* , \"1\"")]
	public async Task Put_WithMalformedPrecondition_ShouldAnswer400(string name, string value)
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);

		var response = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), request => request.Headers.TryAddWithoutValidation(name, value));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await SendAsync(harness, HttpMethod.Get, device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	/// <summary>
	/// Verifies that If-Match and If-None-Match together are 400.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_WithBothPreconditions_ShouldAnswer400()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);

		var response = await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), request =>
		{
			request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
			request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	/// <summary>
	/// Verifies that an invalid document is 400 with the reasons and stores nothing: a schema version other than 1, a place outside
	/// Brussels, an unknown fuel and a body that is not a document.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_InvalidDocument_ShouldAnswer400AndStoreNothing()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		var valid = Document();
		var documents = new[]
		{
			valid with { SchemaVersion = 2 },
			valid with { Home = valid.Home! with { Latitude = 48.0 } },
			valid with { Vehicle = valid.Vehicle! with { Fuel = "Steam" } },
		};

		foreach (var document in documents)
		{
			var response = await SendAsync(harness, HttpMethod.Put, device.AccessToken, document, CreateOnly);

			response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
			(await response.Content.ReadAsStringAsync()).Should().NotContain(SecretAddress, "an answer never repeats the address");
		}

		using var broken = new StringContent("{\"schemaVersion\":1}", Encoding.UTF8, "application/json");
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = broken };
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", device.AccessToken);
		request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
		(await harness.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await SendAsync(harness, HttpMethod.Get, device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	/// <summary>
	/// Verifies that a body over 40 KB is answered 413 whatever it holds, and stores nothing. It runs on a real Kestrel server,
	/// because the in-memory server does not enforce the limit.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_OversizedBody_ShouldAnswer413()
	{
		await using var harness = await SignInHarness.StartAsync(server, useKestrel: true);
		var device = await harness.SignInAsync(Subject);
		using var body = new StringContent("{\"schemaVersion\":1,\"padding\":\"" + new string('a', 45000) + "\"}", Encoding.UTF8, "application/json");
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = body };
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", device.AccessToken);
		request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);

		var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
		await using var context = harness.OpenContext();
		(await context.Set<AccountProfile>().CountAsync()).Should().Be(0);
	}
	#endregion

	#region Storage Tests
	/// <summary>
	/// Verifies what is at rest: the payload does not hold the address or any readable JSON, the key identifier is set, and the
	/// revision and the schema version are those of the writes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_ShouldStoreOnlyEncryptedBytes()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		_ = (await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), CreateOnly)).EnsureSuccessStatusCode();
		_ = (await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), IfMatch("\"1\""))).EnsureSuccessStatusCode();

		await using var context = harness.OpenContext();
		var row = await context.Set<AccountProfile>().SingleAsync();

		row.Revision.Should().Be(2);
		row.SchemaVersion.Should().Be(1);
		row.KeyId.Should().MatchRegex("^[0-9a-f]{8}$");
		Encoding.UTF8.GetString(row.Payload).Should().NotContain(SecretAddress).And.NotContain("schemaVersion");
	}

	/// <summary>
	/// Verifies that a stored payload that was changed outside the API is answered 500 without a cause, and that the answer holds
	/// nothing of the key or the profile.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_WhenThePayloadWasTamperedWith_ShouldAnswer500WithoutACause()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		_ = (await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), CreateOnly)).EnsureSuccessStatusCode();
		await using (var owner = harness.OpenContext())
		{
			_ = await owner.Database.ExecuteSqlRawAsync("UPDATE account_profiles SET \"Payload\" = decode('00112233445566778899aabbccddeeff00112233445566778899aabb', 'hex')");
		}

		var response = await SendAsync(harness, HttpMethod.Get, device.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		var text = await response.Content.ReadAsStringAsync();
		using var problem = JsonDocument.Parse(text);
		problem.RootElement.TryGetProperty("detail", out _).Should().BeFalse();
		text.Should().NotContain("key", "the answer says nothing of the cause").And.NotContain(SecretAddress);
	}
	#endregion

	#region Limit And Erasure Tests
	/// <summary>
	/// Verifies the per-account profile limit: a burst of 10 is served, the next request is 429 with Retry-After, another account is
	/// not affected, and the limit is apart from the limit of the account endpoint.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_BeyondTheBurst_ShouldAnswer429ForThatAccountOnly()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var busy = await harness.SignInAsync(Subject);
		var other = await harness.SignInAsync(OtherSubject);

		var statuses = new List<HttpStatusCode>();
		HttpResponseMessage? last = null;
		for (var request = 0; request < 14; request++)
		{
			last = await SendAsync(harness, HttpMethod.Get, busy.AccessToken);
			statuses.Add(last.StatusCode);
		}

		statuses.Take(10).Should().AllBeEquivalentTo(HttpStatusCode.NotFound, "the burst of 10 is served");
		statuses.Skip(10).Should().Contain(HttpStatusCode.TooManyRequests);
		last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		last.Headers.RetryAfter.Should().NotBeNull();
		(await SendAsync(harness, HttpMethod.Get, other.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await harness.GetMeAsync(busy.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the profile limit is not the account limit");
	}

	/// <summary>
	/// Verifies that after the erasure of the account the profile is gone from the database and the old access token is refused
	/// with 401 on both endpoints.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Profile_AfterErasure_ShouldAnswer401AndLeaveNoRow()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		_ = (await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), CreateOnly)).EnsureSuccessStatusCode();

		(await harness.DeleteAccountAsync(device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await SendAsync(harness, HttpMethod.Get, device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await SendAsync(harness, HttpMethod.Put, device.AccessToken, Document(), CreateOnly)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		await using var context = harness.OpenContext();
		(await context.Set<AccountProfile>().CountAsync()).Should().Be(0);
	}

	/// <summary>
	/// Verifies that two creates at the same moment, both having read the account before either saves and so both finding no
	/// profile, give one 200 and one 412 and not a 500, and that one profile at revision 1 remains.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_TwoCreatesAtOnce_ShouldAnswerOne200AndOne412()
	{
		using var rendezvous = new Barrier(2);
		await using var harness = await SignInHarness.StartAsync(server, configureServices: services => RendezvousRepository.Install(services, rendezvous, RendezvousRepository.HeldRead.ById));
		var device = await harness.SignInAsync(Subject);

		var responses = await Task.WhenAll(
			SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("A"), CreateOnly),
			SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("B"), CreateOnly));

		responses.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.PreconditionFailed]);
		await using var context = harness.OpenContext();
		(await context.Set<AccountProfile>().SingleAsync()).Revision.Should().Be(1);
	}

	/// <summary>
	/// Verifies that two replacements of the same revision at the same moment give one 200 and one 412, and that the revision grows
	/// by one only.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Put_TwoReplacementsAtOnce_ShouldAnswerOne200AndOne412()
	{
		using var rendezvous = new Barrier(2);
		await using var harness = await SignInHarness.StartAsync(server, configureServices: services => RendezvousRepository.Install(services, rendezvous, RendezvousRepository.HeldRead.ById));
		var device = await harness.SignInAsync(Subject);
		_ = await SeedByRaceAsync(harness, device.AccessToken);

		var responses = await Task.WhenAll(
			SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("A"), IfMatch("\"1\"")),
			SendAsync(harness, HttpMethod.Put, device.AccessToken, Document("B"), IfMatch("\"1\"")));

		responses.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.PreconditionFailed]);
		await using var context = harness.OpenContext();
		(await context.Set<AccountProfile>().SingleAsync()).Revision.Should().Be(2);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a valid document with every section set; the secret address is in the home.
	/// </summary>
	/// <param name="label">The label of the home.</param>
	/// <returns>The document.</returns>
	private static ProfileDocument Document(string label = "Home")
	{
		return new ProfileDocument(
			ProfileDocument.SupportedSchemaVersion,
			new ProfilePlaceSection(label, 50.8467, 4.3525, SecretAddress, Moment),
			new ProfilePlaceSection("Work", 50.8503, 4.3517, null, Moment),
			new ProfileFavouritesSection(Moment, [new ProfileFavouritePlace("Gym", 50.85, 4.35, "Rue Haute 1")]),
			new ProfileVehicleSection("renault", "Zoe", 2021, "Electric", "Unknown", ["Type2", "Ccs"], 52.0, 300, "1-ABC-123", Moment),
			new ProfileCardSection("Bruxelles", "A1", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), ["T1"], ["M1"], ["Lim1"], "12345", "1-ABC-123", Moment));
	}

	/// <summary>
	/// Adds the header that asks to create.
	/// </summary>
	/// <param name="request">The request.</param>
	private static void CreateOnly(HttpRequestMessage request)
	{
		request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
	}

	/// <summary>
	/// Builds the change that adds an If-Match header.
	/// </summary>
	/// <param name="tag">The entity tag with its quotes.</param>
	/// <returns>The change to a request.</returns>
	private static Action<HttpRequestMessage> IfMatch(string tag)
	{
		return request => request.Headers.IfMatch.Add(new EntityTagHeaderValue(tag));
	}

	/// <summary>
	/// Sends a request to the profile endpoint.
	/// </summary>
	/// <param name="harness">The harness.</param>
	/// <param name="method">The HTTP method.</param>
	/// <param name="accessToken">The access token; <see langword="null"/> for none.</param>
	/// <param name="document">The body of a write; <see langword="null"/> for none.</param>
	/// <param name="configure">Changes the request, for example to add a header.</param>
	/// <returns>The answer.</returns>
	private static Task<HttpResponseMessage> SendAsync(SignInHarness harness, HttpMethod method, string? accessToken, ProfileDocument? document = null, Action<HttpRequestMessage>? configure = null)
	{
		var request = new HttpRequestMessage(method, ProfileRoute);
		if (accessToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		}

		if (document is not null)
		{
			request.Content = JsonContent.Create(document);
		}

		configure?.Invoke(request);
		return harness.Client.SendAsync(request);
	}

	/// <summary>
	/// Creates the first profile of an account on a harness whose account reads wait at a barrier for a partner: two creates are sent
	/// together so that neither waits alone, and one of them wins.
	/// </summary>
	/// <param name="harness">The harness.</param>
	/// <param name="accessToken">The access token.</param>
	/// <returns>The answer of the create that won.</returns>
	private static async Task<HttpResponseMessage> SeedByRaceAsync(SignInHarness harness, string accessToken)
	{
		var first = SendAsync(harness, HttpMethod.Put, accessToken, Document(), CreateOnly);
		var second = SendAsync(harness, HttpMethod.Put, accessToken, Document(), CreateOnly);
		var both = await Task.WhenAll(first, second).ConfigureAwait(false);
		return both.First(response => response.IsSuccessStatusCode);
	}
	#endregion
}
