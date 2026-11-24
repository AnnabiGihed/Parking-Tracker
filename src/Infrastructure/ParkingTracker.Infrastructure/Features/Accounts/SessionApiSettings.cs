namespace ParkingTracker.Infrastructure.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Where the API of the account lives. The host registers its own value for the build; without one the production address is used.
/// </summary>
/// <param name="BaseAddress">The address of the API, ending with a slash.</param>
public sealed record SessionApiSettings(Uri BaseAddress)
{
	#region Constants
	/// <summary>
	/// The address of the production API.
	/// </summary>
	public const string ProductionAddress = "https://api.parking.pivotsoftwares.com/";
	#endregion

	#region Static Instances
	/// <summary>
	/// The production API.
	/// </summary>
	public static readonly SessionApiSettings Default = new(new Uri(ProductionAddress));
	#endregion
}
