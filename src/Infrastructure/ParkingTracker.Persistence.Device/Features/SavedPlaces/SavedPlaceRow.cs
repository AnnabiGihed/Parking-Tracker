using SQLite;

namespace ParkingTracker.Persistence.Device.Features.SavedPlaces;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stored form of one saved place (table <c>saved_places</c>). A persistence detail: it never leaves the project.
///              The coordinates hold the encrypted value; the position keeps the order in which the places were saved, and the
///              list identifier keeps the identity of the list across loads.
/// </summary>
[Table("saved_places")]
internal sealed class SavedPlaceRow
{
	#region Properties
	/// <summary>
	/// Gets or sets the identifier of the place.
	/// </summary>
	[PrimaryKey]
	[Column("id")]
	public string Id { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the identifier of the list that the place belongs to.
	/// </summary>
	[Column("list_id")]
	public string ListId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the order in the list, from 0.
	/// </summary>
	[Column("position")]
	public int Position { get; set; }

	/// <summary>
	/// Gets or sets the kind of place as the number of the enumeration.
	/// </summary>
	[Column("kind")]
	public int Kind { get; set; }

	/// <summary>
	/// Gets or sets the label.
	/// </summary>
	[Column("label")]
	public string Label { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the address in words.
	/// </summary>
	[Column("address_text")]
	public string AddressText { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the encrypted latitude and longitude.
	/// </summary>
	[Column("coordinates")]
	public string Coordinates { get; set; } = string.Empty;
	#endregion
}
