using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.Errors;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.Vehicles.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="Vehicle"/>: a profile is only created with a listed make, a short model, a plausible year and
///              known enum values; only a vehicle that plugs in has plugs, a battery and a range within their bounds; the values are
///              normalised; an update keeps the identifier and leaves the profile unchanged on failure; and a restore applies the same
///              rules except the latest model year.
/// </summary>
public sealed class VehicleTests
{
	#region Constants
	/// <summary>
	/// The current year the tests pass in.
	/// </summary>
	private const int CurrentYear = 2026;
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Builds the values of a diesel car with a plate typed with spaces and dashes.
	/// </summary>
	/// <returns>The values.</returns>
	private static VehicleDetails Diesel() => new("volkswagen", "  Golf  ", 2015, FuelType.Diesel, EuroNorm.Euro6, Plate: "1-abc 234");

	/// <summary>
	/// Builds the values of an electric car with two plugs, a battery and a range.
	/// </summary>
	/// <returns>The values.</returns>
	private static VehicleDetails Electric() => new("tesla", "Model 3", 2023, FuelType.Electric, EuroNorm.Unknown, new ChargingDetails([ChargingConnector.Ccs, ChargingConnector.Type2], 57.5, 491));

	/// <summary>
	/// Builds the values of an electric car with the given charging details.
	/// </summary>
	/// <param name="charging">The charging details.</param>
	/// <returns>The values.</returns>
	private static VehicleDetails ElectricWith(ChargingDetails charging) => Electric() with { Charging = charging };
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that a diesel car is created with a trimmed model and a normalised plate, and has no charging details.
	/// </summary>
	[Fact]
	public void Create_Diesel_ShouldNormaliseModelAndPlate()
	{
		var result = Vehicle.Create(Diesel(), CurrentYear);

		result.IsSuccess.Should().BeTrue();
		result.Value.Id.Value.Should().NotBe(Guid.Empty);
		result.Value.Model.Should().Be("Golf");
		result.Value.Plate!.Value.Should().Be("1ABC234");
		result.Value.PlugsIn.Should().BeFalse();
		result.Value.ToDetails().Should().BeEquivalentTo(new VehicleDetails("volkswagen", "Golf", 2015, FuelType.Diesel, EuroNorm.Euro6, null, "1ABC234"));
	}

	/// <summary>
	/// Verifies that an electric car keeps its plugs once each and in order, its battery and its range.
	/// </summary>
	[Fact]
	public void Create_Electric_ShouldKeepTheChargingDetailsInOrder()
	{
		var result = Vehicle.Create(ElectricWith(new ChargingDetails([ChargingConnector.Ccs, ChargingConnector.Type2, ChargingConnector.Ccs], 57.5, 491)), CurrentYear);

		result.IsSuccess.Should().BeTrue();
		result.Value.PlugsIn.Should().BeTrue();
		result.Value.Connectors.Should().Equal(ChargingConnector.Type2, ChargingConnector.Ccs);
		result.Value.BatteryKwh.Should().Be(57.5);
		result.Value.RangeKm.Should().Be(491);
		result.Value.Plate.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a plug-in hybrid without any charging details is accepted and gives no charging details back.
	/// </summary>
	[Fact]
	public void Create_PlugInHybridWithoutCharging_ShouldGiveNoChargingBack()
	{
		var result = Vehicle.Create(new VehicleDetails("toyota", null, 2020, FuelType.PlugInHybrid, EuroNorm.Euro6), CurrentYear);

		result.IsSuccess.Should().BeTrue();
		result.Value.Model.Should().BeEmpty();
		result.Value.ToDetails().Charging.Should().BeNull();
	}

	/// <summary>
	/// Verifies that the make must be a code of the curated list, compared exactly.
	/// </summary>
	/// <param name="make">The make code.</param>
	[Theory]
	[InlineData("trabant")]
	[InlineData("Tesla")]
	[InlineData("")]
	public void Create_MakeNotListed_ShouldFail(string make)
	{
		var result = Vehicle.Create(Diesel() with { Make = make }, CurrentYear);

		result.Error.Should().Be(VehicleErrors.MakeUnknown);
	}

	/// <summary>
	/// Verifies that "Other" is a valid make for a vehicle that is not listed.
	/// </summary>
	[Fact]
	public void Create_OtherMake_ShouldSucceed()
	{
		Vehicle.Create(Diesel() with { Make = VehicleMakes.Other, Model = "Trabant 601" }, CurrentYear).IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a model of 60 characters is accepted and one of 61 is refused, white space around it not counting.
	/// </summary>
	[Fact]
	public void Create_ModelLength_ShouldBeAtMostSixty()
	{
		var sixty = Vehicle.Create(Diesel() with { Model = "  " + new string('a', 60) + "  " }, CurrentYear);
		var sixtyOne = Vehicle.Create(Diesel() with { Model = new string('a', 61) }, CurrentYear);

		sixty.IsSuccess.Should().BeTrue();
		sixtyOne.Error.Should().Be(VehicleErrors.ModelTooLong);
	}

	/// <summary>
	/// Verifies the bounds of the model year: 1950 to the year after the current one.
	/// </summary>
	/// <param name="year">The model year.</param>
	/// <param name="accepted">Whether the year is accepted.</param>
	[Theory]
	[InlineData(1949, false)]
	[InlineData(1950, true)]
	[InlineData(2027, true)]
	[InlineData(2028, false)]
	public void Create_Year_ShouldBeFrom1950ToNextYear(int year, bool accepted)
	{
		var result = Vehicle.Create(Diesel() with { Year = year }, CurrentYear);

		result.IsSuccess.Should().Be(accepted);
		if (!accepted)
		{
			result.Error.Code.Should().Be("Vehicle.Year.OutOfRange");
			result.Error.Message.Should().Contain("2027");
		}
	}

	/// <summary>
	/// Verifies that a fuel or an emission standard that is not a known value is refused.
	/// </summary>
	[Fact]
	public void Create_UnknownEnumValues_ShouldFail()
	{
		Vehicle.Create(Diesel() with { Fuel = (FuelType)99 }, CurrentYear).Error.Should().Be(VehicleErrors.FuelUnknown);
		Vehicle.Create(Diesel() with { EuroNorm = (EuroNorm)7 }, CurrentYear).Error.Should().Be(VehicleErrors.EuroNormUnknown);
		Vehicle.Create(ElectricWith(new ChargingDetails([(ChargingConnector)9])), CurrentYear).Error.Should().Be(VehicleErrors.ConnectorUnknown);
	}

	/// <summary>
	/// Verifies that a vehicle that does not plug in cannot have charging details.
	/// </summary>
	/// <param name="fuel">The fuel.</param>
	[Theory]
	[InlineData(FuelType.Petrol)]
	[InlineData(FuelType.Diesel)]
	[InlineData(FuelType.Hybrid)]
	[InlineData(FuelType.Gas)]
	public void Create_ChargingOnAVehicleThatDoesNotPlugIn_ShouldFail(FuelType fuel)
	{
		var result = Vehicle.Create(Electric() with { Fuel = fuel }, CurrentYear);

		result.Error.Should().Be(VehicleErrors.ChargingNotAllowed);
	}

	/// <summary>
	/// Verifies the bounds of the battery: 1 to 250 kWh.
	/// </summary>
	/// <param name="battery">The battery in kWh.</param>
	/// <param name="accepted">Whether the battery is accepted.</param>
	[Theory]
	[InlineData(0.9, false)]
	[InlineData(1.0, true)]
	[InlineData(250.0, true)]
	[InlineData(250.1, false)]
	[InlineData(double.NaN, false)]
	public void Create_Battery_ShouldBeFromOneTo250(double battery, bool accepted)
	{
		var result = Vehicle.Create(ElectricWith(new ChargingDetails([], battery)), CurrentYear);

		result.IsSuccess.Should().Be(accepted);
		if (!accepted)
		{
			result.Error.Should().Be(VehicleErrors.BatteryOutOfRange);
		}
	}

	/// <summary>
	/// Verifies the bounds of the range: 10 to 1000 km.
	/// </summary>
	/// <param name="range">The range in kilometres.</param>
	/// <param name="accepted">Whether the range is accepted.</param>
	[Theory]
	[InlineData(9, false)]
	[InlineData(10, true)]
	[InlineData(1000, true)]
	[InlineData(1001, false)]
	public void Create_Range_ShouldBeFromTenTo1000(int range, bool accepted)
	{
		var result = Vehicle.Create(ElectricWith(new ChargingDetails([], RangeKm: range)), CurrentYear);

		result.IsSuccess.Should().Be(accepted);
		if (!accepted)
		{
			result.Error.Should().Be(VehicleErrors.RangeOutOfRange);
		}
	}

	/// <summary>
	/// Verifies that missing values are rejected with an exception, as a programming error.
	/// </summary>
	[Fact]
	public void Factories_NullArguments_ShouldThrow()
	{
		var noDetails = () => Vehicle.Create(null!, CurrentYear); // null on purpose: proves the guard clause rejects it
		var noId = () => Vehicle.Restore(null!, Diesel()); // null on purpose: proves the guard clause rejects it
		var noRestored = () => Vehicle.Restore(VehicleId.New(), null!); // null on purpose: proves the guard clause rejects it

		noDetails.Should().Throw<ArgumentNullException>();
		noId.Should().Throw<ArgumentNullException>();
		noRestored.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Update Tests
	/// <summary>
	/// Verifies that an update replaces every value and keeps the identifier.
	/// </summary>
	[Fact]
	public void Update_ValidValues_ShouldReplaceThemAndKeepTheIdentifier()
	{
		var vehicle = Vehicle.Create(Diesel(), CurrentYear).Value;
		var id = vehicle.Id;

		var result = vehicle.Update(Electric(), CurrentYear);

		result.IsSuccess.Should().BeTrue();
		vehicle.Id.Should().Be(id);
		vehicle.Make.Should().Be("tesla");
		vehicle.Fuel.Should().Be(FuelType.Electric);
		vehicle.Plate.Should().BeNull();
		vehicle.Connectors.Should().Equal(ChargingConnector.Type2, ChargingConnector.Ccs);
	}

	/// <summary>
	/// Verifies that an update that breaks a rule fails and leaves the profile as it was.
	/// </summary>
	[Fact]
	public void Update_InvalidValues_ShouldFailAndKeepTheOldValues()
	{
		var vehicle = Vehicle.Create(Diesel(), CurrentYear).Value;
		var before = vehicle.ToDetails();

		var result = vehicle.Update(Electric() with { Year = 1900 }, CurrentYear);

		result.IsFailure.Should().BeTrue();
		vehicle.ToDetails().Should().BeEquivalentTo(before);
	}

	/// <summary>
	/// Verifies that an update without values is rejected with an exception.
	/// </summary>
	[Fact]
	public void Update_NullDetails_ShouldThrow()
	{
		var vehicle = Vehicle.Create(Diesel(), CurrentYear).Value;

		var update = () => vehicle.Update(null!, CurrentYear); // null on purpose: proves the guard clause rejects it

		update.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Restore Tests
	/// <summary>
	/// Verifies that a restored profile keeps its stored identifier and values.
	/// </summary>
	[Fact]
	public void Restore_StoredValues_ShouldKeepTheIdentifier()
	{
		var id = new VehicleId(Guid.Parse("55555555-5555-5555-5555-555555555555"));

		var result = Vehicle.Restore(id, Electric());

		result.IsSuccess.Should().BeTrue();
		result.Value.Id.Should().Be(id);
		result.Value.ToDetails().Should().BeEquivalentTo(Vehicle.Create(Electric(), CurrentYear).Value.ToDetails());
	}

	/// <summary>
	/// Verifies that a stored profile that breaks a rule is refused.
	/// </summary>
	[Fact]
	public void Restore_StoredValuesBreakARule_ShouldFail()
	{
		var result = Vehicle.Restore(VehicleId.New(), Diesel() with { Make = "atlantis" });

		result.Error.Should().Be(VehicleErrors.MakeUnknown);
	}

	/// <summary>
	/// Verifies that a restore checks the earliest model year but not the latest, which was checked when the profile was saved.
	/// </summary>
	[Fact]
	public void Restore_YearBounds_ShouldCheckOnlyTheEarliestYear()
	{
		Vehicle.Restore(VehicleId.New(), Diesel() with { Year = 2099 }).IsSuccess.Should().BeTrue();
		Vehicle.Restore(VehicleId.New(), Diesel() with { Year = 1949 }).Error.Code.Should().Be("Vehicle.Year.OutOfRange");
	}
	#endregion

	#region Make List Tests
	/// <summary>
	/// Verifies that the codes of the curated list are unique, lower case and end with "Other".
	/// </summary>
	[Fact]
	public void VehicleMakes_Codes_ShouldBeUniqueLowerCaseAndEndWithOther()
	{
		var codes = VehicleMakes.All.Select(make => make.Code).ToList();

		codes.Should().OnlyHaveUniqueItems();
		codes.Should().OnlyContain(code => !code.Any(char.IsUpper));
		codes[^1].Should().Be(VehicleMakes.Other);
		VehicleMakes.IsKnown(null).Should().BeFalse();
	}
	#endregion
}
