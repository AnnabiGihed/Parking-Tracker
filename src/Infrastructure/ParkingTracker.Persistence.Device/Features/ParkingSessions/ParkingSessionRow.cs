using SQLite;

namespace ParkingTracker.Persistence.Device.Features.ParkingSessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stored form of the parking session (table <c>parking_sessions</c>). A persistence detail: it never leaves the
///              project. The coordinates hold the encrypted value; moments are milliseconds since 1970, as the saved car and timer
///              of the first version wrote them.
/// </summary>
[Table("parking_sessions")]
internal sealed class ParkingSessionRow
{
	#region Properties
	/// <summary>
	/// Gets or sets the identifier of the session.
	/// </summary>
	[PrimaryKey]
	[Column("id")]
	public string Id { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the encrypted latitude and longitude.
	/// </summary>
	[Column("coordinates")]
	public string Coordinates { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets when the car was parked.
	/// </summary>
	[Column("parked_at")]
	public long ParkedAt { get; set; }

	/// <summary>
	/// Gets or sets the headline the app showed.
	/// </summary>
	[Column("headline")]
	public string Headline { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets where the car is, in words.
	/// </summary>
	[Column("address_text")]
	public string AddressText { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets when the timer ends, or <see langword="null"/> when none runs.
	/// </summary>
	[Column("timer_end")]
	public long? TimerEnd { get; set; }

	/// <summary>
	/// Gets or sets the length of the timer in minutes, or <see langword="null"/> when none runs.
	/// </summary>
	[Column("timer_minutes")]
	public int? TimerMinutes { get; set; }

	/// <summary>
	/// Gets or sets when the user left the car, or <see langword="null"/> while the session is open.
	/// </summary>
	[Column("ended_at")]
	public long? EndedAt { get; set; }
	#endregion
}
