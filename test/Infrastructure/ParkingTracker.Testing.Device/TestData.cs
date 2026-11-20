using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.ParkingSessions.Aggregates;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;

namespace ParkingTracker.Testing.Device;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The municipalities, cards, points, sessions and vehicle profiles that the persistence tests share, built through
///              the aggregates' own factories so that a test never holds a value the domain would refuse.
/// </summary>
public static class TestData
{
	#region Static Instances
	/// <summary>
	/// The City of Brussels: a street-list municipality that allows a sector.
	/// </summary>
	public static readonly Municipality City = new("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null);

	/// <summary>
	/// Ixelles: a municipality that issues cards per area, with the prefix IXLR and the border zone LIM_X.
	/// </summary>
	public static readonly Municipality Ixelles = new("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR"], ["LIM_X"], null);

	/// <summary>
	/// A point in the Grand-Place of Brussels.
	/// </summary>
	public static readonly GeoPoint GrandPlace = new(50.8467, 4.3525);

	/// <summary>
	/// The moment the parked-car tests start at.
	/// </summary>
	public static readonly DateTimeOffset Parked = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates a card of the City of Brussels with a sector, a number, a plate with spaces and a tolerance.
	/// </summary>
	/// <returns>The card.</returns>
	public static ResidentCard CityCard()
	{
		var card = new ParkingCard("bruxelles", "louise", new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)), ["BXL_LIM_IXL_LOUISE"], [], [], " B012-345-678 ", "1 abc-234");
		return ResidentCard.Create(City, card).Value;
	}

	/// <summary>
	/// Creates a card of Ixelles with an area, a border zone, no number, no plate and an open period.
	/// </summary>
	/// <returns>The card.</returns>
	public static ResidentCard IxellesCard()
	{
		var card = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXLR_1", "IXLR"], ["LIM_X"]);
		return ResidentCard.Create(Ixelles, card).Value;
	}

	/// <summary>
	/// Creates an open session with a headline and an address, and a timer when asked.
	/// </summary>
	/// <param name="timer">The timer to start, or <see langword="null"/> for none.</param>
	/// <returns>The session.</returns>
	public static ParkingSession Session(ParkingTimer? timer = null)
	{
		var session = ParkingSession.Create(GrandPlace, Parked, "Paid parking", "Rue de la Loi").Value;
		if (timer is not null && session.StartTimer(timer).IsFailure)
		{
			throw new ArgumentException("The timer does not fit the session.", nameof(timer));
		}

		return session;
	}

	/// <summary>
	/// Creates the profile of an electric car with two plugs, a battery, a range and a plate typed with a dash.
	/// </summary>
	/// <returns>The profile.</returns>
	public static Vehicle ElectricVehicle()
	{
		var details = new VehicleDetails("tesla", "Model 3", 2023, FuelType.Electric, EuroNorm.Unknown, new ChargingDetails([ChargingConnector.Ccs, ChargingConnector.Type2], 57.5, 491), "2-xyz-987");
		return Vehicle.Create(details, 2026).Value;
	}

	/// <summary>
	/// Creates the profile of a petrol car without a model and without a plate.
	/// </summary>
	/// <returns>The profile.</returns>
	public static Vehicle PetrolVehicle()
	{
		return Vehicle.Create(new VehicleDetails("other", null, 1990, FuelType.Petrol, EuroNorm.Euro1), 2026).Value;
	}
	#endregion
}
