using SQLite;

namespace ParkingTracker.Persistence.Device.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stored form of the sync state (table <c>sync_state</c>, schema step 3): the one row with the base revision, the
///              changed flag and the moment each synced section last changed, in milliseconds since 1970. A persistence detail that never
///              leaves the project.
/// </summary>
[Table("sync_state")]
internal sealed class SyncStateRow
{
	#region Properties
	/// <summary>
	/// Gets or sets the identifier of the row, which is always 1.
	/// </summary>
	[PrimaryKey]
	[Column("id")]
	public int Id { get; set; } = 1;

	/// <summary>
	/// Gets or sets the revision of the server copy the phone last agreed with, or <see langword="null"/>.
	/// </summary>
	[Column("base_revision")]
	public long? BaseRevision { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the phone changed a section since the base revision.
	/// </summary>
	[Column("dirty")]
	public int Dirty { get; set; }

	/// <summary>
	/// Gets or sets the moment the home place last changed.
	/// </summary>
	[Column("home_updated_at")]
	public long? HomeUpdatedAt { get; set; }

	/// <summary>
	/// Gets or sets the moment the work place last changed.
	/// </summary>
	[Column("work_updated_at")]
	public long? WorkUpdatedAt { get; set; }

	/// <summary>
	/// Gets or sets the moment the list of favourites last changed.
	/// </summary>
	[Column("favourites_updated_at")]
	public long? FavouritesUpdatedAt { get; set; }

	/// <summary>
	/// Gets or sets the moment the vehicle last changed.
	/// </summary>
	[Column("vehicle_updated_at")]
	public long? VehicleUpdatedAt { get; set; }

	/// <summary>
	/// Gets or sets the moment the residents' card last changed.
	/// </summary>
	[Column("card_updated_at")]
	public long? CardUpdatedAt { get; set; }
	#endregion
}
