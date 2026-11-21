namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A clock for the API under test that starts at the real time and moves only when a test advances it, so the sliding
///              and the absolute expiry of a session can be crossed without waiting. It is registered in place of the system clock
///              through the container; the real time of the database and of Google ID tokens is not affected.
/// </summary>
public sealed class AdjustableClock : TimeProvider
{
	#region Fields
	/// <summary>
	/// The time added to the real time.
	/// </summary>
	private TimeSpan offset = TimeSpan.Zero;
	#endregion

	#region Public Methods
	/// <summary>
	/// Moves the clock forward.
	/// </summary>
	/// <param name="time">The time to add.</param>
	public void Advance(TimeSpan time)
	{
		offset += time;
	}

	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow()
	{
		return base.GetUtcNow() + offset;
	}
	#endregion
}
