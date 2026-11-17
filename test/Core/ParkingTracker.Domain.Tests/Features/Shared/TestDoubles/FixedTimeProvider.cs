namespace ParkingTracker.Domain.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A <see cref="TimeProvider"/> that always reports the same instant and a chosen local time zone, so that
///              tests can fix "now" without a package for it.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
	#region Fields
	/// <summary>
	/// The fixed moment.
	/// </summary>
	private readonly DateTimeOffset utcNow;

	/// <summary>
	/// The time zone the fixed moment is read in.
	/// </summary>
	private readonly TimeZoneInfo localZone;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FixedTimeProvider"/> class.
	/// </summary>
	/// <param name="utcNow">The instant to report.</param>
	/// <param name="localZone">The local time zone; UTC when omitted.</param>
	public FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localZone = null)
	{
		this.utcNow = utcNow;
		this.localZone = localZone ?? TimeZoneInfo.Utc;
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow() => utcNow;

	/// <inheritdoc />
	public override TimeZoneInfo LocalTimeZone => localZone;
	#endregion
}
