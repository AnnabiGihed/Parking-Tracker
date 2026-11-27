using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stored form of the device owner (table <c>device_owner</c>, schema step 1): the one row that says which account the
///              data on the phone belongs to. A persistence detail that never leaves the project.
/// </summary>
[Table("device_owner")]
internal sealed class DeviceOwnerRow
{
	#region Properties
	/// <summary>
	/// Gets or sets the identifier of the row, which is always 1.
	/// </summary>
	[PrimaryKey]
	[Column("id")]
	public int Id { get; set; } = 1;

	/// <summary>
	/// Gets or sets the hash of the account id.
	/// </summary>
	[Column("account_hash")]
	public string AccountHash { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the moment the account was bound, in milliseconds since 1970.
	/// </summary>
	[Column("bound_at")]
	public long BoundAt { get; set; }
	#endregion
}
