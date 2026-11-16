namespace ParkingTracker.Infrastructure.Tests.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A clock that always returns the same instant and reports a given time zone as the local one.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
	#region Fields
	private readonly DateTimeOffset utcNow;
	private readonly TimeZoneInfo localZone;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FixedTimeProvider"/> class.
	/// </summary>
	/// <param name="utcNow">The instant the clock shows.</param>
	/// <param name="localZone">The local time zone.</param>
	public FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localZone)
	{
		this.utcNow = utcNow;
		this.localZone = localZone;
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow() => utcNow;

	/// <inheritdoc />
	public override TimeZoneInfo LocalTimeZone => localZone;
	#endregion
}
