using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.Errors;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using Pivot.Framework.Domain.Primitives;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.Vehicles.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The one vehicle profile of the user on this device (ADR-0031): make, model, year, fuel, emission standard, how it
///              charges and its plate, so that the app can later suggest where to charge or park and warn about the Brussels
///              low-emission zone. The profile enforces the curated fields (a listed make, a short model, a plausible year, plugs and
///              a battery only for a vehicle that plugs in) and bridges to the <see cref="VehicleDetails"/> record the screens and the
///              device database use. The current year is passed in, so the aggregate does not read the clock.
/// </summary>
public sealed class Vehicle : LightweightAggregateRoot<VehicleId>
{
	#region Constants
	/// <summary>
	/// The earliest model year a profile accepts.
	/// </summary>
	public const int EarliestYear = 1950;

	/// <summary>
	/// The longest model text, in characters after trimming.
	/// </summary>
	public const int ModelMaximumLength = 60;

	/// <summary>
	/// The smallest battery, in kWh.
	/// </summary>
	public const double MinimumBatteryKwh = 1;

	/// <summary>
	/// The largest battery, in kWh.
	/// </summary>
	public const double MaximumBatteryKwh = 250;

	/// <summary>
	/// The shortest range on a full battery, in kilometres.
	/// </summary>
	public const int MinimumRangeKm = 10;

	/// <summary>
	/// The longest range on a full battery, in kilometres.
	/// </summary>
	public const int MaximumRangeKm = 1000;
	#endregion

	#region Properties
	/// <summary>
	/// Gets the code of the make, one of <see cref="VehicleMakes.All"/>.
	/// </summary>
	public string Make { get; private set; }

	/// <summary>
	/// Gets the model, trimmed; empty when unknown.
	/// </summary>
	public string Model { get; private set; }

	/// <summary>
	/// Gets the model year.
	/// </summary>
	public int Year { get; private set; }

	/// <summary>
	/// Gets what the vehicle runs on.
	/// </summary>
	public FuelType Fuel { get; private set; }

	/// <summary>
	/// Gets the emission standard, or <see cref="EuroNorm.Unknown"/>.
	/// </summary>
	public EuroNorm EuroNorm { get; private set; }

	/// <summary>
	/// Gets the plugs the vehicle takes, each once and in ascending order; empty for a vehicle that does not plug in.
	/// </summary>
	public IReadOnlyCollection<ChargingConnector> Connectors { get; private set; }

	/// <summary>
	/// Gets the usable battery in kWh, or <see langword="null"/> when unknown or the vehicle does not plug in.
	/// </summary>
	public double? BatteryKwh { get; private set; }

	/// <summary>
	/// Gets the range on a full battery in kilometres, or <see langword="null"/> when unknown or the vehicle does not plug in.
	/// </summary>
	public int? RangeKm { get; private set; }

	/// <summary>
	/// Gets the number plate, or <see langword="null"/> when none was entered. It is the vehicle's own, apart from the plate on
	/// the residents' card.
	/// </summary>
	public LicencePlate? Plate { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the vehicle charges from a plug: an electric vehicle or a plug-in hybrid.
	/// </summary>
	public bool PlugsIn => IsPlugIn(Fuel);
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="Vehicle"/> class from values that passed the checks.
	/// </summary>
	/// <param name="id">The identifier of the profile.</param>
	/// <param name="details">The checked values.</param>
	private Vehicle(VehicleId id, VehicleDetails details)
		: base(id)
	{
		Make = string.Empty;
		Model = string.Empty;
		Connectors = [];
		Apply(details);
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the vehicle profile from the values the user entered. The model is trimmed and the plate put in capitals without
	/// spaces or dashes.
	/// </summary>
	/// <param name="details">The values as entered.</param>
	/// <param name="currentYear">The current calendar year, which bounds the model year.</param>
	/// <returns>The profile, or the first rule the values break (see <see cref="VehicleErrors"/>).</returns>
	/// <exception cref="ArgumentNullException"><paramref name="details"/> is <see langword="null"/>.</exception>
	public static Result<Vehicle> Create(VehicleDetails details, int currentYear)
	{
		ArgumentNullException.ThrowIfNull(details);
		var check = Check(details, currentYear + 1);
		return check.IsFailure ? Result.Failure<Vehicle>(check.Error, check.ResultExceptionType) : new Vehicle(VehicleId.New(), details);
	}

	/// <summary>
	/// Rebuilds a profile that was stored before, with its stored identifier and the checks of <see cref="Create"/> except the
	/// latest model year: that bound was checked when the profile was saved, and checking it again against today's clock would
	/// lock the user out of a valid profile when the phone's clock goes back a year. It raises no event: the profile existed already.
	/// </summary>
	/// <param name="id">The stored identifier of the profile.</param>
	/// <param name="details">The stored values.</param>
	/// <returns>The profile, or the first rule the stored values break.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static Result<Vehicle> Restore(VehicleId id, VehicleDetails details)
	{
		ArgumentNullException.ThrowIfNull(id);
		ArgumentNullException.ThrowIfNull(details);
		var check = Check(details, latestYear: null);
		return check.IsFailure ? Result.Failure<Vehicle>(check.Error, check.ResultExceptionType) : new Vehicle(id, details);
	}
	#endregion

	#region Domain Behaviours
	/// <summary>
	/// Replaces every value of the profile, as when the user edits it or changes car; the profile keeps its identifier.
	/// </summary>
	/// <param name="details">The new values as entered.</param>
	/// <param name="currentYear">The current calendar year, which bounds the model year.</param>
	/// <returns>Success, or the first rule the values break; the profile then keeps its old values.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="details"/> is <see langword="null"/>.</exception>
	public Result Update(VehicleDetails details, int currentYear)
	{
		ArgumentNullException.ThrowIfNull(details);
		var check = Check(details, currentYear + 1);
		if (check.IsFailure)
		{
			return check;
		}

		Apply(details);
		return Result.Success();
	}

	/// <summary>
	/// Gives the profile in the shape that the screens and the device database use.
	/// </summary>
	/// <returns>A <see cref="VehicleDetails"/> with the normalised values; <see cref="VehicleDetails.Charging"/> is
	/// <see langword="null"/> when the vehicle does not plug in or nothing about its charging is known.</returns>
	public VehicleDetails ToDetails()
	{
		var charging = Connectors.Count == 0 && BatteryKwh is null && RangeKm is null ? null : new ChargingDetails(Connectors, BatteryKwh, RangeKm);
		return new VehicleDetails(Make, Model, Year, Fuel, EuroNorm, charging, Plate?.Value);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Takes over values that passed <see cref="Check"/>, normalising the model, the plugs and the plate.
	/// </summary>
	/// <param name="details">The checked values.</param>
	private void Apply(VehicleDetails details)
	{
		Make = details.Make;
		Model = (details.Model ?? string.Empty).Trim();
		Year = details.Year;
		Fuel = details.Fuel;
		EuroNorm = details.EuroNorm;
		Connectors = Array.AsReadOnly(details.Charging?.Connectors.Distinct().Order().ToArray() ?? []);
		BatteryKwh = details.Charging?.BatteryKwh;
		RangeKm = details.Charging?.RangeKm;
		Plate = LicencePlate.CreateOptional(details.Plate);
	}

	/// <summary>
	/// Checks the values of a profile, stopping at the first broken rule.
	/// </summary>
	/// <param name="details">The values to check.</param>
	/// <param name="latestYear">The latest model year allowed, or <see langword="null"/> for no upper bound.</param>
	/// <returns>Success, or the first broken rule.</returns>
	private static Result Check(VehicleDetails details, int? latestYear)
	{
		var identity = CheckIdentity(details, latestYear);
		if (identity.IsFailure)
		{
			return identity;
		}

		if (!Enum.IsDefined(details.Fuel))
		{
			return Result.Failure(VehicleErrors.FuelUnknown);
		}

		if (!Enum.IsDefined(details.EuroNorm))
		{
			return Result.Failure(VehicleErrors.EuroNormUnknown);
		}

		return details.Charging is null ? Result.Success() : CheckCharging(details.Fuel, details.Charging);
	}

	/// <summary>
	/// Checks the make, the model and the year.
	/// </summary>
	/// <param name="details">The values to check.</param>
	/// <param name="latestYear">The latest model year allowed, or <see langword="null"/> for no upper bound.</param>
	/// <returns>Success, or the first broken rule.</returns>
	private static Result CheckIdentity(VehicleDetails details, int? latestYear)
	{
		if (!VehicleMakes.IsKnown(details.Make))
		{
			return Result.Failure(VehicleErrors.MakeUnknown);
		}

		if ((details.Model ?? string.Empty).Trim().Length > ModelMaximumLength)
		{
			return Result.Failure(VehicleErrors.ModelTooLong);
		}

		if (details.Year < EarliestYear)
		{
			return Result.Failure(VehicleErrors.YearOutOfRange(latestYear));
		}

		return details.Year > latestYear ? Result.Failure(VehicleErrors.YearOutOfRange(latestYear)) : Result.Success();
	}

	/// <summary>
	/// Checks that only a vehicle that plugs in has charging details, and that the plugs, the battery and the range are valid.
	/// </summary>
	/// <param name="fuel">What the vehicle runs on.</param>
	/// <param name="charging">The charging details.</param>
	/// <returns>Success, or the first broken rule.</returns>
	private static Result CheckCharging(FuelType fuel, ChargingDetails charging)
	{
		if (!IsPlugIn(fuel))
		{
			return Result.Failure(VehicleErrors.ChargingNotAllowed);
		}

		if (charging.Connectors is null || charging.Connectors.Any(connector => !Enum.IsDefined(connector)))
		{
			return Result.Failure(VehicleErrors.ConnectorUnknown);
		}

		if (charging.BatteryKwh is { } battery && !(battery >= MinimumBatteryKwh && battery <= MaximumBatteryKwh))
		{
			return Result.Failure(VehicleErrors.BatteryOutOfRange);
		}

		return charging.RangeKm is { } range && (range < MinimumRangeKm || range > MaximumRangeKm)
			? Result.Failure(VehicleErrors.RangeOutOfRange)
			: Result.Success();
	}

	/// <summary>
	/// Tells whether a fuel charges from a plug.
	/// </summary>
	/// <param name="fuel">The fuel.</param>
	/// <returns><see langword="true"/> for an electric vehicle or a plug-in hybrid.</returns>
	private static bool IsPlugIn(FuelType fuel) => fuel is FuelType.Electric or FuelType.PlugInHybrid;
	#endregion
}
