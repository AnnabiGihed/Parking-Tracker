using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using SavedPlaceEntity = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlace;
using SavedPlacesList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests of <see cref="ProfileDocumentValidator"/> and <see cref="SaveProfileCommandValidator"/> (ADR-0032): a valid
///              document passes, and each limit is checked at its edge, the edge itself being valid and the step beyond it not. The
///              validator has no port; only the clock is a fake.
/// </summary>
public sealed class ProfileDocumentValidatorTests
{
	#region Fields
	/// <summary>
	/// The clock the validator reads the year and the moment from.
	/// </summary>
	private readonly FakeTimeProvider clock = new(ProfileTestData.Now);

	/// <summary>
	/// The validator under test.
	/// </summary>
	private readonly ProfileDocumentValidator validator;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileDocumentValidatorTests"/> class.
	/// </summary>
	public ProfileDocumentValidatorTests()
	{
		validator = new ProfileDocumentValidator(clock);
	}
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Gets the four corners of the Brussels box.
	/// </summary>
	public static TheoryData<double, double> BoxCorners
	{
		get
		{
			var box = SavedPlacesList.BrusselsBox;
			return new TheoryData<double, double>
			{
				{ box.South, box.West },
				{ box.South, box.East },
				{ box.North, box.West },
				{ box.North, box.East },
			};
		}
	}
	#endregion

	#region Valid Document Tests
	/// <summary>
	/// Verifies that a complete document inside every limit passes.
	/// </summary>
	[Fact]
	public void Validate_CompleteDocument_ShouldPass()
	{
		validator.TestValidate(ProfileTestData.Valid()).ShouldNotHaveAnyValidationErrors();
	}

	/// <summary>
	/// Verifies that a document with every optional section missing and no favourites passes: a section may never have been set.
	/// </summary>
	[Fact]
	public void Validate_WithoutOptionalSections_ShouldPass()
	{
		var document = new ProfileDocument(1, null, null, new ProfileFavouritesSection(ProfileTestData.Now, []), null, null);

		validator.TestValidate(document).ShouldNotHaveAnyValidationErrors();
	}
	#endregion

	#region Schema And Place Tests
	/// <summary>
	/// Verifies that only schema version 1 is accepted.
	/// </summary>
	/// <param name="version">The schema version.</param>
	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	[InlineData(-1)]
	public void Validate_UnsupportedSchemaVersion_ShouldFail(int version)
	{
		var result = validator.TestValidate(ProfileTestData.Valid() with { SchemaVersion = version });

		result.ShouldHaveValidationErrorFor(document => document.SchemaVersion);
	}

	/// <summary>
	/// Verifies that the corners of the Brussels box are inside, edges included.
	/// </summary>
	/// <param name="latitude">The latitude.</param>
	/// <param name="longitude">The longitude.</param>
	[Theory]
	[MemberData(nameof(BoxCorners))]
	public void Validate_PlaceOnTheEdgeOfTheBox_ShouldPass(double latitude, double longitude)
	{
		var document = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { Latitude = latitude, Longitude = longitude } };

		validator.TestValidate(document).ShouldNotHaveAnyValidationErrors();
	}

	/// <summary>
	/// Verifies that a coordinate a little beyond the box is refused, for the home and for a favourite.
	/// </summary>
	/// <param name="latitude">The latitude.</param>
	/// <param name="longitude">The longitude.</param>
	[Theory]
	[InlineData(50.7499, 4.35)]
	[InlineData(50.9301, 4.35)]
	[InlineData(50.85, 4.2299)]
	[InlineData(50.85, 4.4901)]
	public void Validate_PlaceOutsideTheBox_ShouldFail(double latitude, double longitude)
	{
		var home = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { Latitude = latitude, Longitude = longitude } };
		var favourite = ProfileTestData.Valid() with { Favourites = new ProfileFavouritesSection(ProfileTestData.Now, [new ProfileFavouritePlace(null, latitude, longitude, null)]) };

		validator.TestValidate(home).IsValid.Should().BeFalse();
		validator.TestValidate(favourite).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies the length of a label: 40 characters pass, 41 do not.
	/// </summary>
	[Fact]
	public void Validate_LabelLength_ShouldStopAtTheDomainLimit()
	{
		var edge = ProfileTestData.Valid() with { Work = ProfileTestData.Place() with { Label = new string('a', SavedPlaceEntity.MaximumLabelLength) } };
		var beyond = ProfileTestData.Valid() with { Work = ProfileTestData.Place() with { Label = new string('a', SavedPlaceEntity.MaximumLabelLength + 1) } };

		validator.TestValidate(edge).IsValid.Should().BeTrue();
		validator.TestValidate(beyond).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies the length of an address text: 200 characters pass, 201 do not.
	/// </summary>
	[Fact]
	public void Validate_AddressTextLength_ShouldStopAt200()
	{
		var edge = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { AddressText = new string('a', 200) } };
		var beyond = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { AddressText = new string('a', 201) } };

		validator.TestValidate(edge).IsValid.Should().BeTrue();
		validator.TestValidate(beyond).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that 50 favourites pass and 51 do not.
	/// </summary>
	[Fact]
	public void Validate_FavouriteCount_ShouldStopAt50()
	{
		var edge = ProfileTestData.Valid() with { Favourites = Favourites(50) };
		var beyond = ProfileTestData.Valid() with { Favourites = Favourites(51) };

		validator.TestValidate(edge).IsValid.Should().BeTrue();
		validator.TestValidate(beyond).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a time more than a day ahead of the server is refused and a time exactly a day ahead is not.
	/// </summary>
	[Fact]
	public void Validate_UpdatedAtAhead_ShouldAllowOneDay()
	{
		var edge = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { UpdatedAt = ProfileTestData.Now.AddDays(1) } };
		var beyond = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { UpdatedAt = ProfileTestData.Now.AddDays(1).AddSeconds(1) } };

		validator.TestValidate(edge).IsValid.Should().BeTrue();
		validator.TestValidate(beyond).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the moments of cleared sections are accepted for absent sections, that a document without them stays valid, and that
	/// a time exactly a day ahead passes and one second more does not.
	/// </summary>
	[Fact]
	public void Validate_ClearedSections_ShouldBeAcceptedForAbsentSectionsOnly()
	{
		var absent = ProfileTestData.Valid() with { Home = null, Card = null, Cleared = new ProfileClearedSections(ProfileTestData.Now, null, null, ProfileTestData.Now.AddDays(1)) };
		var present = ProfileTestData.Valid() with { Cleared = new ProfileClearedSections(ProfileTestData.Now, null, null, null) };
		var presentVehicle = ProfileTestData.Valid() with { Cleared = new ProfileClearedSections(null, null, ProfileTestData.Now, null) };
		var ahead = ProfileTestData.Valid() with { Home = null, Cleared = new ProfileClearedSections(ProfileTestData.Now.AddDays(1).AddSeconds(1), null, null, null) };
		var empty = ProfileTestData.Valid() with { Cleared = new ProfileClearedSections(null, null, null, null) };

		validator.TestValidate(ProfileTestData.Valid() with { Cleared = null }).IsValid.Should().BeTrue();
		validator.TestValidate(absent).IsValid.Should().BeTrue();
		validator.TestValidate(empty).IsValid.Should().BeTrue();
		validator.TestValidate(present).ShouldHaveValidationErrorFor(item => item.Cleared);
		validator.TestValidate(presentVehicle).ShouldHaveValidationErrorFor(item => item.Cleared);
		validator.TestValidate(ahead).ShouldHaveValidationErrorFor(item => item.Cleared);
	}

	/// <summary>
	/// Verifies that a favourites section the caller left out is refused instead of throwing.
	/// </summary>
	[Fact]
	public void Validate_MissingFavourites_ShouldFail()
	{
		var document = ProfileTestData.Valid() with { Favourites = null! };

		validator.TestValidate(document).ShouldHaveValidationErrorFor(item => item.Favourites);
	}
	#endregion

	#region Vehicle Tests
	/// <summary>
	/// Verifies the model year: the earliest year and next year pass, the year before the earliest and two years ahead do not.
	/// </summary>
	[Fact]
	public void Validate_VehicleYear_ShouldFollowTheDomainAndTheClock()
	{
		var year = clock.GetUtcNow().Year;

		VehicleIsValid(vehicle => vehicle with { Year = Vehicle.EarliestYear }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { Year = year + 1 }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { Year = Vehicle.EarliestYear - 1 }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { Year = year + 2 }).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the fuel, the norm and the connectors must be names of the domain enumerations; numbers and other case are refused.
	/// </summary>
	/// <param name="fuel">The fuel.</param>
	/// <param name="norm">The emission norm.</param>
	/// <param name="connector">The connector.</param>
	/// <param name="valid">Whether the vehicle is valid.</param>
	[Theory]
	[InlineData("Electric", "Euro6", "Ccs", true)]
	[InlineData("PlugInHybrid", "Unknown", "Chademo", true)]
	[InlineData("electric", "Euro6", "Ccs", false)]
	[InlineData("4", "Euro6", "Ccs", false)]
	[InlineData("Steam", "Euro6", "Ccs", false)]
	[InlineData("Electric", "Euro9", "Ccs", false)]
	[InlineData("Electric", "Euro6", "Tesla", false)]
	[InlineData("Electric", "Euro6", "0", false)]
	public void Validate_EnumerationNames_ShouldMatchTheDomain(string fuel, string norm, string connector, bool valid)
	{
		var document = ProfileTestData.Valid() with { Vehicle = ProfileTestData.Vehicle() with { Fuel = fuel, EuroNorm = norm, Connectors = [connector] } };

		validator.TestValidate(document).IsValid.Should().Be(valid);
	}

	/// <summary>
	/// Verifies the limits of the make, the model, the battery, the range, the plate and the connectors at their edges.
	/// </summary>
	[Fact]
	public void Validate_VehicleLimits_ShouldBeInclusiveAtTheEdges()
	{
		VehicleIsValid(vehicle => vehicle with { Make = new string('a', 60) }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { Make = new string('a', 61) }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { Make = string.Empty }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { Model = new string('a', Vehicle.ModelMaximumLength) }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { Model = new string('a', Vehicle.ModelMaximumLength + 1) }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { BatteryKwh = 0 }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { BatteryKwh = Vehicle.MaximumBatteryKwh }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { BatteryKwh = Vehicle.MaximumBatteryKwh + 0.1 }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { BatteryKwh = -0.1 }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { RangeKm = Vehicle.MaximumRangeKm }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { RangeKm = Vehicle.MaximumRangeKm + 1 }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { RangeKm = -1 }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { Plate = new string('a', 16) }).Should().BeTrue();
		VehicleIsValid(vehicle => vehicle with { Plate = new string('a', 17) }).Should().BeFalse();
		VehicleIsValid(vehicle => vehicle with { Connectors = Enumerable.Repeat("Ccs", 21).ToList() }).Should().BeFalse();
	}
	#endregion

	#region Card Tests
	/// <summary>
	/// Verifies the limits of the municipality, the sector, the number and the plate of the card at their edges.
	/// </summary>
	[Fact]
	public void Validate_CardTextLimits_ShouldBeInclusiveAtTheEdges()
	{
		CardIsValid(card => card with { Municipality = new string('a', 64) }).Should().BeTrue();
		CardIsValid(card => card with { Municipality = new string('a', 65) }).Should().BeFalse();
		CardIsValid(card => card with { Municipality = string.Empty }).Should().BeFalse();
		CardIsValid(card => card with { Sector = new string('a', 32) }).Should().BeTrue();
		CardIsValid(card => card with { Sector = new string('a', 33) }).Should().BeFalse();
		CardIsValid(card => card with { Number = new string('a', 32) }).Should().BeTrue();
		CardIsValid(card => card with { Number = new string('a', 33) }).Should().BeFalse();
		CardIsValid(card => card with { Plate = new string('a', 17) }).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that each list of the card has at most 20 items of at most 32 characters.
	/// </summary>
	[Fact]
	public void Validate_CardLists_ShouldLimitTheItemsAndTheirLength()
	{
		var twenty = Enumerable.Repeat("x", 20).ToList();
		var twentyOne = Enumerable.Repeat("x", 21).ToList();
		var longest = new List<string> { new('a', 32) };
		var tooLong = new List<string> { new('a', 33) };

		CardIsValid(card => card with { Tolerances = twenty, Mailles = twenty, Lims = twenty }).Should().BeTrue();
		CardIsValid(card => card with { Tolerances = longest, Mailles = longest, Lims = longest }).Should().BeTrue();
		CardIsValid(card => card with { Tolerances = twentyOne }).Should().BeFalse();
		CardIsValid(card => card with { Mailles = twentyOne }).Should().BeFalse();
		CardIsValid(card => card with { Lims = twentyOne }).Should().BeFalse();
		CardIsValid(card => card with { Tolerances = tooLong }).Should().BeFalse();
		CardIsValid(card => card with { Mailles = tooLong }).Should().BeFalse();
		CardIsValid(card => card with { Lims = tooLong }).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the card may be valid from the day it is valid to, not after it, and that one date alone is fine.
	/// </summary>
	[Fact]
	public void Validate_CardDates_ShouldRequireFromNotAfterTo()
	{
		var day = new DateOnly(2026, 6, 1);

		CardIsValid(card => card with { ValidFrom = day, ValidTo = day }).Should().BeTrue();
		CardIsValid(card => card with { ValidFrom = day, ValidTo = day.AddDays(-1) }).Should().BeFalse();
		CardIsValid(card => card with { ValidFrom = null, ValidTo = day }).Should().BeTrue();
		CardIsValid(card => card with { ValidFrom = day, ValidTo = null }).Should().BeTrue();
	}
	#endregion

	#region Command Validator Tests
	/// <summary>
	/// Verifies that the command validator checks the document and refuses a revision below 1.
	/// </summary>
	[Fact]
	public void CommandValidator_ShouldCheckTheDocumentAndTheRevision()
	{
		var commandValidator = new SaveProfileCommandValidator(clock);
		var account = Guid.NewGuid();

		commandValidator.TestValidate(new SaveProfileCommand(account, ProfileTestData.Valid(), ProfileWriteCondition.CreateOnly)).IsValid.Should().BeTrue();
		commandValidator.TestValidate(new SaveProfileCommand(account, ProfileTestData.Valid(), ProfileWriteCondition.ForRevision(1))).IsValid.Should().BeTrue();
		commandValidator.TestValidate(new SaveProfileCommand(account, ProfileTestData.Valid(), ProfileWriteCondition.ForRevision(0))).IsValid.Should().BeFalse();
		commandValidator.TestValidate(new SaveProfileCommand(account, ProfileTestData.Valid() with { SchemaVersion = 2 }, ProfileWriteCondition.CreateOnly)).IsValid.Should().BeFalse();
		commandValidator.TestValidate(new SaveProfileCommand(account, null!, ProfileWriteCondition.CreateOnly)).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the message of a refusal does not repeat the value that was refused, so a log or an answer holds no address.
	/// </summary>
	[Fact]
	public void Validate_Messages_ShouldNotRepeatTheValue()
	{
		var document = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { AddressText = new string('Z', 201) } };

		var result = validator.Validate(document);

		result.Errors.Should().NotBeEmpty();
		result.Errors.Should().OnlyContain(error => !error.ErrorMessage.Contains("ZZZ", StringComparison.Ordinal));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a favourites section with a number of places.
	/// </summary>
	/// <param name="count">The number of places.</param>
	/// <returns>The section.</returns>
	private static ProfileFavouritesSection Favourites(int count)
	{
		return new ProfileFavouritesSection(ProfileTestData.Now, Enumerable.Range(0, count).Select(_ => new ProfileFavouritePlace("F", 50.85, 4.35, null)).ToList());
	}

	/// <summary>
	/// Tells whether the valid document with a changed vehicle is valid.
	/// </summary>
	/// <param name="change">Changes the vehicle.</param>
	/// <returns><see langword="true"/> when the document is valid.</returns>
	private bool VehicleIsValid(Func<ProfileVehicleSection, ProfileVehicleSection> change)
	{
		return validator.Validate(ProfileTestData.Valid() with { Vehicle = change(ProfileTestData.Vehicle()) }).IsValid;
	}

	/// <summary>
	/// Tells whether the valid document with a changed card is valid.
	/// </summary>
	/// <param name="change">Changes the card.</param>
	/// <returns><see langword="true"/> when the document is valid.</returns>
	private bool CardIsValid(Func<ProfileCardSection, ProfileCardSection> change)
	{
		return validator.Validate(ProfileTestData.Valid() with { Card = change(ProfileTestData.Card()) }).IsValid;
	}
	#endregion
}
