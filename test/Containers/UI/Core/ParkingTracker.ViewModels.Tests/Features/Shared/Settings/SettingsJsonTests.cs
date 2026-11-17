using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SettingsJson"/> against sample values as <c>app.js</c> wrote them to <c>localStorage</c>: they are
///              read unchanged, a card without a municipality is a City of Brussels card, unreadable text gives "nothing saved", and what
///              is written has the property names and order of the first version.
/// </summary>
public sealed class SettingsJsonTests
{
	#region Card Tests
	/// <summary>
	/// Verifies that a full card as the card dialog of <c>app.js</c> saved it is read field by field.
	/// </summary>
	[Fact]
	public void ParseCard_FullCardFromAppJs_ShouldReadEveryField()
	{
		const string json = """{"number":"B000-1","plate":"1ABC234","municipality":"ixelles","sector":null,"from":"2026-01-01","to":"2027-01-31","tolerances":["IXL_LOUISE"],"mailles":["IXL01"],"lims":["ETT02"]}""";

		var card = SettingsJson.ParseCard(json);

		card.Should().NotBeNull();
		card.Municipality.Should().Be("ixelles");
		card.Sector.Should().BeNull();
		card.Period.Should().Be(new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 31)));
		card.Tolerances.Should().Equal("IXL_LOUISE");
		card.Mailles.Should().Equal("IXL01");
		card.Lims.Should().Equal("ETT02");
		card.Number.Should().Be("B000-1");
		card.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that the card of the first version, which had no municipality, reads as a City of Brussels card.
	/// </summary>
	[Fact]
	public void ParseCard_CardWithoutMunicipality_ShouldBeABruxellesCard()
	{
		const string json = """{"number":"TEST","plate":"1TST000","sector":"louise","from":"2026-01-01","to":"2099-12-31","tolerances":[]}""";

		var card = SettingsJson.ParseCard(json);

		card!.Municipality.Should().Be("bruxelles");
		card.Sector.Should().Be("louise");
		card.Mailles.Should().BeEmpty();
		card.Lims.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that empty dates, number and plate, as an unfilled form saved them, read as "none".
	/// </summary>
	[Fact]
	public void ParseCard_EmptyFields_ShouldReadAsNone()
	{
		const string json = """{"number":"","plate":"","municipality":"bruxelles","sector":"louise","from":"","to":"","tolerances":[],"mailles":[],"lims":[]}""";

		var card = SettingsJson.ParseCard(json);

		card!.Number.Should().BeNull();
		card.Plate.Should().BeNull();
		card.Period.Should().Be(new CardPeriod(null, null));
	}

	/// <summary>
	/// Verifies that missing, unreadable or non-object text gives no card instead of an exception.
	/// </summary>
	/// <param name="json">The saved text.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("null")]
	[InlineData("[1,2]")]
	[InlineData("{not json")]
	public void ParseCard_NothingUsable_ShouldGiveNoCard(string? json)
	{
		SettingsJson.ParseCard(json).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a written card has the property order of <c>app.js</c> and survives a round trip, with accents unescaped.
	/// </summary>
	[Fact]
	public void SerializeCard_ThenParse_ShouldKeepTheCardAndTheOldShape()
	{
		var card = new ParkingCard("bruxelles", "louise", new CardPeriod(new DateOnly(2026, 1, 1), null), ["IXL_LOUISE"], [], [], "Nº é", "1ABC234");

		var json = SettingsJson.SerializeCard(card);

		json.Should().Be("""{"number":"Nº é","plate":"1ABC234","municipality":"bruxelles","sector":"louise","from":"2026-01-01","to":"","tolerances":["IXL_LOUISE"],"mailles":[],"lims":[]}""");
		SettingsJson.ParseCard(json).Should().BeEquivalentTo(card);
	}

	/// <summary>
	/// Verifies that a card without a sector is written with <c>null</c> as <c>app.js</c> did.
	/// </summary>
	[Fact]
	public void SerializeCard_NoSector_ShouldWriteNull()
	{
		var card = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXL01"], [], null, null);

		SettingsJson.SerializeCard(card).Should().Contain("\"sector\":null").And.Contain("\"number\":\"\"");
	}
	#endregion

	#region Car Tests
	/// <summary>
	/// Verifies that a car saved by the dialog of <c>app.js</c>, with milliseconds since 1970 and the HTML of the place, is read unchanged.
	/// </summary>
	[Fact]
	public void ParseCar_CarFromAppJs_ShouldReadPositionTimeAndTexts()
	{
		const string json = """{"lat":50.82655,"lon":4.36505,"t":1763373600000,"status":"Free for you here","where":"<b>Avenue Louise 216</b>"}""";

		var car = SettingsJson.ParseCar(json);

		car.Should().Be(new ParkedCar(new GeoPoint(50.82655, 4.36505), DateTimeOffset.FromUnixTimeMilliseconds(1763373600000), "Free for you here", "<b>Avenue Louise 216</b>"));
	}

	/// <summary>
	/// Verifies that a car without a position is not a car.
	/// </summary>
	/// <param name="json">The saved text.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("""{"t":1}""")]
	[InlineData("""{"lat":"x","lon":4}""")]
	[InlineData("oops")]
	public void ParseCar_NoPosition_ShouldGiveNoCar(string? json)
	{
		SettingsJson.ParseCar(json).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a written car has the property names of <c>app.js</c>.
	/// </summary>
	[Fact]
	public void SerializeCar_ShouldUseTheOldPropertyNames()
	{
		var car = new ParkedCar(new GeoPoint(50.5, 4.25), DateTimeOffset.FromUnixTimeMilliseconds(1000), "PAY", string.Empty);

		SettingsJson.SerializeCar(car).Should().Be("""{"lat":50.5,"lon":4.25,"t":1000,"status":"PAY","where":""}""");
	}
	#endregion

	#region Layers Tests
	/// <summary>
	/// Verifies that the switches of <c>app.js</c> are read, and that a missing switch keeps its default.
	/// </summary>
	[Fact]
	public void ParseLayers_PartialObject_ShouldKeepDefaultsForMissingSwitches()
	{
		var layers = SettingsJson.ParseLayers("""{"streets":false,"carparks":true}""");

		layers.Should().Be(new LayerSettings(Streets: false, CarParks: true));
	}

	/// <summary>
	/// Verifies that nothing saved or unreadable text gives the defaults.
	/// </summary>
	/// <param name="json">The saved text.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("garbage")]
	[InlineData("5")]
	public void ParseLayers_NothingUsable_ShouldGiveTheDefaults(string? json)
	{
		SettingsJson.ParseLayers(json).Should().Be(new LayerSettings());
	}

	/// <summary>
	/// Verifies that the switches are written under the keys of <c>app.js</c>, with <c>carparks</c> in lower case.
	/// </summary>
	[Fact]
	public void SerializeLayers_ShouldUseTheOldKeys()
	{
		SettingsJson.SerializeLayers(new LayerSettings()).Should().Be("""{"streets":true,"spaces":true,"works":true,"carparks":false,"chargers":false}""");
	}
	#endregion

	#region Permission Snooze Tests
	/// <summary>
	/// Verifies that the number of <c>Date.now() + 7 days</c> is read as a moment and written back the same.
	/// </summary>
	[Fact]
	public void PermissionSnooze_RoundTrip_ShouldKeepTheMoment()
	{
		var until = DateTimeOffset.FromUnixTimeMilliseconds(1764000000000);

		var json = SettingsJson.SerializePermissionSnooze(until);

		json.Should().Be("1764000000000");
		SettingsJson.ParsePermissionSnooze(json).Should().Be(until);
	}

	/// <summary>
	/// Verifies that missing or non-numeric text gives no moment.
	/// </summary>
	/// <param name="json">The saved text.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("\"soon\"")]
	[InlineData("{")]
	public void ParsePermissionSnooze_NothingUsable_ShouldGiveNoMoment(string? json)
	{
		SettingsJson.ParsePermissionSnooze(json).Should().BeNull();
	}
	#endregion
}
