using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.ParkingDetection.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds a realistic sequence of fixes along a straight street, as the background location request of the
///              app delivers them: at most one fix a second and only after the phone moved 5 m, so a phone that stands
///              still delivers none.
/// </summary>
public sealed class SimulatedTrack
{
	#region Constants
	/// <summary>
	/// Degrees of latitude per metre, on the sphere that <c>GeoMath</c> uses.
	/// </summary>
	private const double DegreesPerMetre = 1 / 111_195.0;

	/// <summary>
	/// The distance in metres the phone must move before the location service delivers the next fix.
	/// </summary>
	private const double MinimumDistanceMetres = 5;

	/// <summary>
	/// The accuracy radius in metres given to every fix.
	/// </summary>
	private const double AccuracyMetres = 6;
	#endregion

	#region Fields
	/// <summary>
	/// The fixes delivered so far, in time order.
	/// </summary>
	private readonly List<GpsFix> fixes = [];

	/// <summary>
	/// The metres travelled north of the origin.
	/// </summary>
	private double travelledMetres;

	/// <summary>
	/// The metres travelled north of the origin at the last delivered fix.
	/// </summary>
	private double deliveredMetres = double.NegativeInfinity;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SimulatedTrack"/> class.
	/// </summary>
	/// <param name="start">The moment the track starts.</param>
	/// <param name="origin">The place the track starts from.</param>
	public SimulatedTrack(DateTimeOffset start, GeoPoint origin)
	{
		Now = start;
		Origin = origin;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the place the track starts from.
	/// </summary>
	public GeoPoint Origin { get; }

	/// <summary>
	/// Gets the moment the track has reached.
	/// </summary>
	public DateTimeOffset Now { get; private set; }

	/// <summary>
	/// Gets the place the track has reached.
	/// </summary>
	public GeoPoint Position => new(Origin.Latitude + (travelledMetres * DegreesPerMetre), Origin.Longitude);

	/// <summary>
	/// Gets the fixes delivered so far, in time order.
	/// </summary>
	public IReadOnlyList<GpsFix> Fixes => fixes;
	#endregion

	#region Public Methods
	/// <summary>
	/// Moves north at a constant speed, delivering a fix each second the phone has moved 5 m since the last one.
	/// </summary>
	/// <param name="seconds">How long the phone moves.</param>
	/// <param name="kilometresPerHour">The speed.</param>
	/// <returns>This track, to chain the next stretch.</returns>
	public SimulatedTrack Move(int seconds, double kilometresPerHour)
	{
		var metresPerSecond = kilometresPerHour / 3.6;
		for (var second = 0; second < seconds; second++)
		{
			Now = Now.AddSeconds(1);
			travelledMetres += metresPerSecond;
			if (travelledMetres - deliveredMetres >= MinimumDistanceMetres)
			{
				deliveredMetres = travelledMetres;
				fixes.Add(new GpsFix(Position, AccuracyMetres, metresPerSecond, Now));
			}
		}

		return this;
	}

	/// <summary>
	/// Stands still; the location service delivers no fix meanwhile.
	/// </summary>
	/// <param name="seconds">How long the phone stands still.</param>
	/// <returns>This track, to chain the next stretch.</returns>
	public SimulatedTrack Stand(int seconds)
	{
		Now = Now.AddSeconds(seconds);
		return this;
	}
	#endregion
}
