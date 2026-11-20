using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Vehicles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stored form of the vehicle profile (table <c>vehicles</c>, schema step 2). A persistence detail: it never leaves the
///              project and the aggregate carries no sqlite-net attributes. The enums are stored as their numbers, the plugs as a JSON
///              array of numbers and the plate as the encrypted value.
/// </summary>
[Table("vehicles")]
internal sealed class VehicleRow
{
	#region Properties
	/// <summary>
	/// Gets or sets the identifier of the profile.
	/// </summary>
	[PrimaryKey]
	[Column("id")]
	public string Id { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the code of the make.
	/// </summary>
	[Column("make")]
	public string Make { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the model; empty when unknown.
	/// </summary>
	[Column("model")]
	public string Model { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the model year.
	/// </summary>
	[Column("model_year")]
	public int ModelYear { get; set; }

	/// <summary>
	/// Gets or sets the number of the fuel.
	/// </summary>
	[Column("fuel")]
	public int Fuel { get; set; }

	/// <summary>
	/// Gets or sets the number of the emission standard.
	/// </summary>
	[Column("euro_norm")]
	public int EuroNorm { get; set; }

	/// <summary>
	/// Gets or sets the numbers of the plugs as a JSON array.
	/// </summary>
	[Column("connectors")]
	public string Connectors { get; set; } = "[]";

	/// <summary>
	/// Gets or sets the usable battery in kWh, or <see langword="null"/>.
	/// </summary>
	[Column("battery_kwh")]
	public double? BatteryKwh { get; set; }

	/// <summary>
	/// Gets or sets the range in kilometres, or <see langword="null"/>.
	/// </summary>
	[Column("range_km")]
	public int? RangeKm { get; set; }

	/// <summary>
	/// Gets or sets the encrypted plate, or <see langword="null"/>.
	/// </summary>
	[Column("plate")]
	public string? Plate { get; set; }
	#endregion
}
