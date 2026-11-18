using SQLite;

namespace ParkingTracker.Persistence.Device.Features.ResidentCards;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stored form of the residents' card (table <c>resident_cards</c>). A persistence detail: it never leaves the
///              project and the aggregate carries no sqlite-net attributes. The number and the plate hold the encrypted value, the
///              code lists are JSON arrays and the dates are <c>yyyy-MM-dd</c> text.
/// </summary>
[Table("resident_cards")]
internal sealed class ResidentCardRow
{
	#region Properties
	/// <summary>
	/// Gets or sets the identifier of the card.
	/// </summary>
	[PrimaryKey]
	[Column("id")]
	public string Id { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the municipality code.
	/// </summary>
	[Column("municipality")]
	public string Municipality { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the sector code, or <see langword="null"/>.
	/// </summary>
	[Column("sector")]
	public string? Sector { get; set; }

	/// <summary>
	/// Gets or sets the first valid day, or <see langword="null"/>.
	/// </summary>
	[Column("valid_from")]
	public string? ValidFrom { get; set; }

	/// <summary>
	/// Gets or sets the last valid day, or <see langword="null"/>.
	/// </summary>
	[Column("valid_to")]
	public string? ValidTo { get; set; }

	/// <summary>
	/// Gets or sets the tolerance codes as a JSON array.
	/// </summary>
	[Column("tolerances")]
	public string Tolerances { get; set; } = "[]";

	/// <summary>
	/// Gets or sets the area codes as a JSON array.
	/// </summary>
	[Column("mailles")]
	public string Mailles { get; set; } = "[]";

	/// <summary>
	/// Gets or sets the border-zone codes as a JSON array.
	/// </summary>
	[Column("lims")]
	public string Lims { get; set; } = "[]";

	/// <summary>
	/// Gets or sets the encrypted card number, or <see langword="null"/>.
	/// </summary>
	[Column("card_number")]
	public string? CardNumber { get; set; }

	/// <summary>
	/// Gets or sets the encrypted plate, or <see langword="null"/>.
	/// </summary>
	[Column("plate")]
	public string? Plate { get; set; }
	#endregion
}
