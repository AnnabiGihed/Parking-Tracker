namespace ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A clock that always says Tuesday 17 November 2026, 10:00 UTC, in the UTC time zone.
/// </summary>
internal sealed class FixedClock : TimeProvider
{
	#region Properties
	/// <inheritdoc />
	public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
	#endregion

	#region TimeProvider Implementation
	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
	#endregion
}
