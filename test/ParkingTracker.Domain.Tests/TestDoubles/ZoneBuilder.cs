using ParkingTracker.Domain.Cards;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Tests.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds street surfaces, municipalities, cards and questions for decision tests, around a fixed place in Ixelles.
/// </summary>
public static class ZoneBuilder
{
	#region Constants
	/// <summary>
	/// The place the tests stand on.
	/// </summary>
	public static readonly GeoPoint Here = new(50.8250, 4.3606);
	#endregion

	#region Public Methods
	/// <summary>
	/// Creates a square street surface centred on a place.
	/// </summary>
	/// <param name="name">The street name in French and Dutch.</param>
	/// <param name="colour">The colour code (<c>typreg</c>).</param>
	/// <param name="zoneCode">The zone code of the rule, or <see langword="null"/>.</param>
	/// <param name="municipality">The municipality as parking.brussels writes it.</param>
	/// <param name="center">The centre; <see cref="Here"/> when omitted.</param>
	/// <param name="halfSideMetres">Half the side of the square in metres.</param>
	/// <param name="id">The identifier of the surface.</param>
	/// <returns>The surface.</returns>
	public static ZoneSurface Surface(string name, int? colour, string? zoneCode = "TST1", string municipality = "IXELLES", GeoPoint? center = null, double halfSideMetres = 20, string id = "1")
	{
		var middle = WebMercator.ToMercator(center ?? Here);
		var half = halfSideMetres / Math.Cos((center ?? Here).Latitude * Math.PI / 180);
		MercatorPoint[] ring =
		[
			new(middle.X - half, middle.Y - half),
			new(middle.X + half, middle.Y - half),
			new(middle.X + half, middle.Y + half),
			new(middle.X - half, middle.Y + half),
			new(middle.X - half, middle.Y - half),
		];
		var shape = new SurfaceShape([[ring]]);
		return new ZoneSurface(id, name, name, municipality, colour, zoneCode, shape);
	}

	/// <summary>
	/// Creates a municipality.
	/// </summary>
	/// <param name="kind">How its card works.</param>
	/// <param name="mu">The name as parking.brussels writes it.</param>
	/// <param name="note">The explanation for kinds that cannot be checked.</param>
	/// <returns>The municipality.</returns>
	public static Municipality Municipality(MunicipalityKind kind, string mu = "IXELLES", string? note = "Check your card.")
	{
		return new Municipality("test", "Testville", "Teststad", mu, kind, [], [], note);
	}

	/// <summary>
	/// Creates a card that is valid for the whole of 2026 and 2027.
	/// </summary>
	/// <param name="municipality">The identifier of its municipality.</param>
	/// <param name="mailles">The zone codes of its areas.</param>
	/// <param name="lims">The zone codes of its border zones.</param>
	/// <returns>The card.</returns>
	public static ParkingCard Card(string municipality = "test", string[]? mailles = null, string[]? lims = null)
	{
		return new ParkingCard(municipality, null, new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)), [], mailles ?? [], lims ?? []);
	}

	/// <summary>
	/// Creates a question at <see cref="Here"/> on a weekday morning with a precise fix.
	/// </summary>
	/// <param name="context">The card and its municipality.</param>
	/// <param name="accuracy">The GPS accuracy.</param>
	/// <param name="isOnline">Whether the device is online.</param>
	/// <param name="now">The moment; Tuesday 17 November 2026, 11:00 in Brussels when omitted.</param>
	/// <returns>The question.</returns>
	public static SpotQuestion Question(DecisionContext context, double? accuracy = 5, bool isOnline = true, DateTimeOffset? now = null)
	{
		return new SpotQuestion(context, Here, accuracy, now ?? new DateTimeOffset(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(1)), isOnline);
	}

	/// <summary>
	/// Creates a context for a municipality kind with the test card.
	/// </summary>
	/// <param name="kind">How the card of the municipality works.</param>
	/// <param name="card">The card; the test card when omitted.</param>
	/// <param name="mu">The municipality as parking.brussels writes it.</param>
	/// <returns>The context.</returns>
	public static DecisionContext Context(MunicipalityKind kind, ParkingCard? card = null, string mu = "IXELLES")
	{
		return new DecisionContext(card ?? Card(), Municipality(kind, mu), null, null);
	}
	#endregion
}
