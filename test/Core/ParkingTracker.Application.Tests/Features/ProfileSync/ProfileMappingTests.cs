using ParkingTracker.Application.Features.ProfileSync.Services;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Application.Tests.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ProfileMapping"/>: each section of the profile document is built from the local data and read back
///              without loss (a round trip per section), a section that never changed carries the epoch, and a value the phone does not know
///              (a fuel by number or an unknown connector) is refused instead of being half applied.
/// </summary>
public sealed class ProfileMappingTests
{
	#region Test Infrastructure
	/// <summary>
	/// The moment of the sections in these tests.
	/// </summary>
	private static readonly DateTimeOffset At = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
	#endregion

	#region Place Tests
	/// <summary>
	/// Verifies that the home place is mapped with its label, point, address and moment, and back into the list.
	/// </summary>
	[Fact]
	public void PlaceSection_RoundTrip_ShouldKeepEveryField()
	{
		var list = PlaceList.Create();
		_ = list.SetHome("Home", new GeoPoint(50.8467, 4.3525), "Grand-Place 1");

		var section = ProfileMapping.ToPlaceSection(list.Home, At);
		var back = PlaceList.Create();
		_ = back.SetHome(section!.Label, new GeoPoint(section.Latitude, section.Longitude), section.AddressText);

		section.Should().Be(new ProfilePlaceSection("Home", 50.8467, 4.3525, "Grand-Place 1", At));
		back.Home!.Point.Should().Be(list.Home!.Point);
		back.Home.AddressText.Should().Be("Grand-Place 1");
		back.Home.Label.Should().Be("Home");
	}

	/// <summary>
	/// Verifies that no place is no section, and that a place that never changed carries the epoch.
	/// </summary>
	[Fact]
	public void PlaceSection_NoPlaceOrNoMoment_ShouldBeNullOrEpoch()
	{
		var list = PlaceList.Create();
		_ = list.SetWork("Work", new GeoPoint(50.85, 4.35), null);

		ProfileMapping.ToPlaceSection(null, At).Should().BeNull();
		ProfileMapping.ToPlaceSection(list.Work, null)!.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch);
	}

	/// <summary>
	/// Verifies that only the favourites are mapped, in the order they were saved, as one section.
	/// </summary>
	[Fact]
	public void FavouritesSection_ShouldHoldOnlyFavouritesInOrder()
	{
		var list = PlaceList.Create();
		_ = list.SetHome("Home", new GeoPoint(50.85, 4.35), null);
		_ = list.AddFavourite("Gym", new GeoPoint(50.84, 4.36), "Rue A");
		_ = list.AddFavourite("Club", new GeoPoint(50.83, 4.37), null);

		var section = ProfileMapping.ToFavouritesSection(list, At);

		section.UpdatedAt.Should().Be(At);
		section.Places.Should().Equal(new ProfileFavouritePlace("Gym", 50.84, 4.36, "Rue A"), new ProfileFavouritePlace("Club", 50.83, 4.37, string.Empty));
		ProfileMapping.ToFavouritesSection(null, null).Places.Should().BeEmpty();
	}
	#endregion

	#region Cleared Tests
	/// <summary>
	/// Verifies that only absent sections with a moment are given a moment of their clear, and that none is none.
	/// </summary>
	[Fact]
	public void ToClearedSections_ShouldHoldTheMomentsOfAbsentSectionsOnly()
	{
		var times = new Application.Features.ProfileSync.Abstractions.SectionTimes(At, At.AddMinutes(1), At.AddMinutes(2), null, At.AddMinutes(3));

		var cleared = ProfileMapping.ToClearedSections(homeAbsent: true, workAbsent: false, vehicleAbsent: true, cardAbsent: true, times);

		cleared.Should().Be(new ProfileClearedSections(At, null, null, At.AddMinutes(3)));
		ProfileMapping.ToClearedSections(false, false, false, false, times).Should().BeNull();
		ProfileMapping.ToClearedSections(true, true, true, true, Application.Features.ProfileSync.Abstractions.SectionTimes.None).Should().BeNull();
	}

	/// <summary>
	/// Verifies the round trip of a clear through the document: the moments written for the absent sections are the moments read back.
	/// </summary>
	[Fact]
	public void ClearedSections_RoundTripThroughTheDocument_ShouldKeepTheMoments()
	{
		var times = new Application.Features.ProfileSync.Abstractions.SectionTimes(At, null, null, At.AddMinutes(1), null);
		var document = new ProfileDocument(
			ProfileDocument.SupportedSchemaVersion,
			null,
			null,
			new ProfileFavouritesSection(DateTimeOffset.UnixEpoch, []),
			null,
			null,
			ProfileMapping.ToClearedSections(true, true, true, true, times));

		ProfileMerge.TimesOf(document).Should().Be(times);
	}
	#endregion

	#region Vehicle Tests
	/// <summary>
	/// Verifies that a vehicle with its charging details is mapped and read back equal.
	/// </summary>
	[Fact]
	public void VehicleSection_WithCharging_ShouldRoundTrip()
	{
		var details = new VehicleDetails("TESLA", "Model 3", 2022, FuelType.Electric, EuroNorm.Euro6, new ChargingDetails([ChargingConnector.Type2, ChargingConnector.Ccs], 75.5, 450), "1ABC123");

		var section = ProfileMapping.ToVehicleSection(details, At);
		var back = ProfileMapping.ToVehicleDetails(section!);

		section!.Fuel.Should().Be("Electric");
		section.Connectors.Should().Equal("Type2", "Ccs");
		back.IsSuccess.Should().BeTrue();
		back.Value.Charging!.Connectors.Should().Equal(ChargingConnector.Type2, ChargingConnector.Ccs);
		back.Value.Should().BeEquivalentTo(details);
	}

	/// <summary>
	/// Verifies that a vehicle without charging details is read back without them.
	/// </summary>
	[Fact]
	public void VehicleSection_WithoutCharging_ShouldRoundTrip()
	{
		var details = new VehicleDetails("OPEL", "Corsa", 2015, FuelType.Petrol, EuroNorm.Euro5, null, null);

		var back = ProfileMapping.ToVehicleDetails(ProfileMapping.ToVehicleSection(details, null)!);

		back.Value.Should().BeEquivalentTo(details);
		ProfileMapping.ToVehicleSection(null, At).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a fuel, a norm or a connector that is not the name of a member is refused, including a number.
	/// </summary>
	[Fact]
	public void ToVehicleDetails_UnknownNames_ShouldFail()
	{
		var valid = new ProfileVehicleSection("OPEL", "Corsa", 2015, "Petrol", "Euro5", [], null, null, null, At);

		ProfileMapping.ToVehicleDetails(valid with { Fuel = "Steam" }).Error.Code.Should().Be(ProfileMapping.InvalidSectionCode);
		ProfileMapping.ToVehicleDetails(valid with { Fuel = "1" }).IsFailure.Should().BeTrue();
		ProfileMapping.ToVehicleDetails(valid with { EuroNorm = "Euro99" }).IsFailure.Should().BeTrue();
		ProfileMapping.ToVehicleDetails(valid with { Connectors = ["Type2", "Plug"] }).IsFailure.Should().BeTrue();
	}
	#endregion

	#region Card Tests
	/// <summary>
	/// Verifies that a card with its dates, lists, number and plate is mapped and read back equal.
	/// </summary>
	[Fact]
	public void CardSection_ShouldRoundTrip()
	{
		var card = new ParkingCard("brussels", "A", new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)), ["T1"], ["M1", "M2"], ["L1"], "1234567", "1ABC123");

		var section = ProfileMapping.ToCardSection(card, At);
		var back = ProfileMapping.ToParkingCard(section!);

		section!.UpdatedAt.Should().Be(At);
		back.Municipality.Should().Be("brussels");
		back.Sector.Should().Be("A");
		back.Period.Should().Be(card.Period);
		back.Tolerances.Should().Equal("T1");
		back.Mailles.Should().Equal("M1", "M2");
		back.Lims.Should().Equal("L1");
		back.Number.Should().Be("1234567");
		back.Plate.Should().Be("1ABC123");
		ProfileMapping.ToCardSection(null, At).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a card without a sector, dates, number and plate round-trips with the optional fields empty.
	/// </summary>
	[Fact]
	public void CardSection_WithoutOptionalFields_ShouldRoundTrip()
	{
		var card = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], [], [], null, null);

		var back = ProfileMapping.ToParkingCard(ProfileMapping.ToCardSection(card, null)!);

		back.Sector.Should().BeNull();
		back.Period.Should().Be(card.Period);
		back.Number.Should().BeNull();
		back.Plate.Should().BeNull();
	}
	#endregion
}
