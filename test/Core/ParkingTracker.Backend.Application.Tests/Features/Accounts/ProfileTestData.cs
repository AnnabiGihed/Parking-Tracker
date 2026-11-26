using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds the profile documents of the profile tests: a complete valid document and its sections.
/// </summary>
internal static class ProfileTestData
{
	#region Static Instances
	/// <summary>
	/// The moment the tests run at.
	/// </summary>
	public static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds a document with every section set and every value inside its limit.
	/// </summary>
	/// <returns>The document.</returns>
	public static ProfileDocument Valid()
	{
		return new ProfileDocument(
			ProfileDocument.SupportedSchemaVersion,
			Place(),
			Place(),
			new ProfileFavouritesSection(Now, [new ProfileFavouritePlace("Gym", 50.85, 4.35, "Rue Haute 1")]),
			Vehicle(),
			Card());
	}

	/// <summary>
	/// Builds a place section inside the Brussels box.
	/// </summary>
	/// <returns>The section.</returns>
	public static ProfilePlaceSection Place()
	{
		return new ProfilePlaceSection("Home", 50.8467, 4.3525, "Rue de la Loi 16", Now);
	}

	/// <summary>
	/// Builds a vehicle section of an electric car.
	/// </summary>
	/// <returns>The section.</returns>
	public static ProfileVehicleSection Vehicle()
	{
		return new ProfileVehicleSection("renault", "Zoe", 2021, "Electric", "Unknown", ["Type2", "Ccs"], 52.0, 300, "1-ABC-123", Now);
	}

	/// <summary>
	/// Builds a card section.
	/// </summary>
	/// <returns>The section.</returns>
	public static ProfileCardSection Card()
	{
		return new ProfileCardSection("Bruxelles", "A1", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), ["T1"], ["M1", "M2"], ["Lim1"], "12345", "1-ABC-123", Now);
	}
	#endregion
}
