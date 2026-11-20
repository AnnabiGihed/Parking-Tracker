namespace ParkingTracker.Domain.Features.Vehicles.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The curated list of makes the user picks from (ADR-0031): the makes common on Belgian roads, sorted by name, and
///              <see cref="Other"/> for any make that is not listed. The list lives in the app rather than in an external vehicle
///              catalogue, so it works offline and sends nothing about the vehicle anywhere. A code is never renamed or removed once
///              released, because the device database stores it; a new make is added with a new code.
/// </summary>
public static class VehicleMakes
{
	#region Constants
	/// <summary>
	/// The code of the make for a vehicle whose make is not listed.
	/// </summary>
	public const string Other = "other";
	#endregion

	#region Static Instances
	/// <summary>
	/// Gets the makes in the order the screens show them: by name, with <see cref="Other"/> last.
	/// </summary>
	public static readonly IReadOnlyList<VehicleMake> All = Array.AsReadOnly(
	[
		new VehicleMake("abarth", "Abarth"),
		new VehicleMake("alfa-romeo", "Alfa Romeo"),
		new VehicleMake("audi", "Audi"),
		new VehicleMake("bmw", "BMW"),
		new VehicleMake("byd", "BYD"),
		new VehicleMake("citroen", "Citroën"),
		new VehicleMake("cupra", "Cupra"),
		new VehicleMake("dacia", "Dacia"),
		new VehicleMake("ds", "DS"),
		new VehicleMake("fiat", "Fiat"),
		new VehicleMake("ford", "Ford"),
		new VehicleMake("honda", "Honda"),
		new VehicleMake("hyundai", "Hyundai"),
		new VehicleMake("jaguar", "Jaguar"),
		new VehicleMake("jeep", "Jeep"),
		new VehicleMake("kia", "Kia"),
		new VehicleMake("land-rover", "Land Rover"),
		new VehicleMake("lexus", "Lexus"),
		new VehicleMake("lynk-co", "Lynk & Co"),
		new VehicleMake("mazda", "Mazda"),
		new VehicleMake("mercedes-benz", "Mercedes-Benz"),
		new VehicleMake("mg", "MG"),
		new VehicleMake("mini", "MINI"),
		new VehicleMake("mitsubishi", "Mitsubishi"),
		new VehicleMake("nissan", "Nissan"),
		new VehicleMake("opel", "Opel"),
		new VehicleMake("peugeot", "Peugeot"),
		new VehicleMake("polestar", "Polestar"),
		new VehicleMake("porsche", "Porsche"),
		new VehicleMake("renault", "Renault"),
		new VehicleMake("seat", "SEAT"),
		new VehicleMake("skoda", "Škoda"),
		new VehicleMake("smart", "smart"),
		new VehicleMake("subaru", "Subaru"),
		new VehicleMake("suzuki", "Suzuki"),
		new VehicleMake("tesla", "Tesla"),
		new VehicleMake("toyota", "Toyota"),
		new VehicleMake("volkswagen", "Volkswagen"),
		new VehicleMake("volvo", "Volvo"),
		new VehicleMake(Other, "Other"),
	]);
	#endregion

	#region Public Methods
	/// <summary>
	/// Tells whether a code is one of the list, compared exactly (codes are lower case).
	/// </summary>
	/// <param name="code">The code to look up, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when a make of <see cref="All"/> has the code.</returns>
	public static bool IsKnown(string? code) => code is not null && All.Any(make => string.Equals(make.Code, code, StringComparison.Ordinal));
	#endregion
}
