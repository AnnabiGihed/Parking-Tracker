using ParkingTracker.Domain.Features.ParkingSessions.Aggregates;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Characterisation tests of the bridges between the aggregates and the saved values: a <c>pt.card</c>, <c>pt.car</c> or
///              <c>pt.timer</c> value read through <see cref="SettingsJson"/> into <see cref="ResidentCard"/> or
///              <see cref="ParkingSession"/> and back is written as the same text, so the storage does not change.
/// </summary>
public sealed class SettingsJsonBridgeTests
{
	#region Static Instances
	/// <summary>
	/// The municipality of the saved card below, a City of Brussels card.
	/// </summary>
	private static readonly Municipality City = new("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null);

	/// <summary>
	/// The municipality of the saved area card below.
	/// </summary>
	private static readonly Municipality Areas = new("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR"], ["LIM_X"], null);
	#endregion

	#region Card Tests
	/// <summary>
	/// Verifies that a City card read from storage goes through the aggregate and is written as the same text.
	/// </summary>
	[Fact]
	public void Card_CityCard_ShouldBeWrittenAsTheSameText()
	{
		const string saved = """{"number":"B000-000-001","plate":"1ABC234","municipality":"bruxelles","sector":"louise","from":"2026-01-01","to":"2026-12-31","tolerances":["IXL_LOUISE"],"mailles":[],"lims":[]}""";
		var card = SettingsJson.ParseCard(saved)!;

		var bridged = ResidentCard.Create(City, card).Value.ToParkingCard();

		bridged.Should().BeEquivalentTo(card);
		SettingsJson.SerializeCard(bridged).Should().Be(SettingsJson.SerializeCard(card));
	}

	/// <summary>
	/// Verifies that an area card without number and plate is written as the same text.
	/// </summary>
	[Fact]
	public void Card_AreaCardWithoutNumberAndPlate_ShouldBeWrittenAsTheSameText()
	{
		const string saved = """{"number":"","plate":"","municipality":"ixelles","sector":null,"from":"2026-01-01","to":"2026-12-31","tolerances":[],"mailles":["IXLR_1"],"lims":["LIM_X"]}""";
		var card = SettingsJson.ParseCard(saved)!;

		var bridged = ResidentCard.Create(Areas, card).Value.ToParkingCard();

		bridged.Should().BeEquivalentTo(card);
		SettingsJson.SerializeCard(bridged).Should().Be(SettingsJson.SerializeCard(card));
	}
	#endregion

	#region Car And Timer Tests
	/// <summary>
	/// Verifies that a saved car and timer go through the session and are written as the same texts.
	/// </summary>
	[Fact]
	public void Session_SavedCarAndTimer_ShouldBeWrittenAsTheSameTexts()
	{
		const string savedCar = """{"lat":50.8503,"lon":4.3517,"t":1794992400000,"status":"Paid parking","where":""}""";
		const string savedTimer = """{"end":1794996000000,"minutes":60}""";
		var car = SettingsJson.ParseCar(savedCar)!;
		var timer = SettingsJson.ParseTimer(savedTimer)!;

		var session = ParkingSession.FromParkedCar(car, timer).Value;

		SettingsJson.SerializeCar(session.ToParkedCar()).Should().Be(SettingsJson.SerializeCar(car));
		SettingsJson.SerializeTimer(session.Timer!).Should().Be(SettingsJson.SerializeTimer(timer));
		session.ToParkedCar().Should().Be(car);
		session.Timer.Should().Be(timer);
	}
	#endregion
}
